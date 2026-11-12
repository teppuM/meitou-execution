using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Kidnap;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;
using Meitou.Simulation.Production;

namespace Execution;

/// <summary>
/// An order to execute a downed character: the nearest of <paramref name="Attackers"/> that can goes. With <see cref="AsJob"/> (Shift), each of
/// them gets the Executing job instead.
/// </summary>
public sealed record ExecuteOrder(IReadOnlyList<CharacterId> Attackers, CharacterId Target) : OrderCommand
{
    /// <summary>
    /// Give the Executing job instead of a one-off order.
    /// </summary>
    public bool AsJob { get; init; }
}

/// <summary>
/// The AI tab's Execute Strangers option set on or off. An order command, so a paused game applies it at once.
/// </summary>
public sealed record SetExecuteStrangers(bool On) : OrderCommand;

/// <summary>
/// Executions: a player character walks to an unconscious (or playing dead) character and chops at it until it dies. Each blow is the game's
/// captive execution blow (<see cref="CombatSystem.Execute"/>: no block or dodge, through armour, with the usual blood, sound, death and lost
/// limbs), one per <c>chop down static</c> clip as the prison executioners swing. Every blow sets a pending Murder against the victim's faction, so
/// the game's witnesses judge it as any crime, and counts with the victim's faction as a blow on a downed body does in a fight
/// (<see cref="FactionDeeds.Blow"/>). Runs on the unused vanilla row 229 <see cref="TaskType.StealthKill"/> as a player order (the Execute menu row)
/// or as the Executing job (Shift + Execute), which finishes off downed bodies near the character whenever the job list reaches it. Walking is
/// <see cref="CharacterTask.GoTo"/>, chopping <see cref="CharacterTask.Idle"/> with the clip; another order or Stop ends it. An execution under way
/// is not saved; the job is (<see cref="JobPatches"/>), and so is the AI tab's Execute Strangers option (<see cref="AiTabPatch"/>), in the mod's own
/// save data.
/// </summary>
public sealed class ExecutionSystem : ITickSystem, IOrderOwner, ITaskOwner, ITaskHandler, IDecisionOwner, IJobSource, IModSaveData
{
    #region Setup

    /// <summary>
    /// The order's and the job's row: 229, built by the original as <c>Task_StealthKO</c> but registered nowhere and started by nothing.
    /// </summary>
    public const TaskType Row = TaskType.StealthKill;

    /// <summary>
    /// The swing clip, the prison executioners' (<see cref="CaptiveSystem.ChopClip"/>).
    /// </summary>
    public const string Clip = CaptiveSystem.ChopClip;

    /// <summary>
    /// How near the victim the executioner must stand to swing, and how far beside it the walk aims.
    /// </summary>
    public const float Reach = 16, StandOff = 8;

    /// <summary>
    /// How far from the character the Executing job looks for bodies.
    /// </summary>
    public const float JobRange = 750;

    /// <summary>
    /// How long the job leaves a body alone after failing to reach it (seconds).
    /// </summary>
    public const float GiveUpSeconds = 60;

    /// <summary>
    /// The fraction of a swing at which its blow lands (the captives' <c>BlowAt</c>).
    /// </summary>
    public const float BlowAt = 0.55f;

    /// <summary>
    /// Walks tried before the executioner gives up on reaching the body, and blows struck before it gives up on killing it.
    /// </summary>
    public const int MaxWalks = 3, MaxBlows = 80;

    /// <summary>
    /// The standard systems it works with: the blow and the relation deeds, the murder, the swing's length, the squads' memory of who fought them.
    /// </summary>
    readonly CombatSystem? _combat;
    readonly CrimeSystem? _crimes;
    readonly CaptiveSystem? _captives;
    readonly DialogueSystem? _dialogue;

    /// <summary>
    /// The executions under way, in the order they began.
    /// </summary>
    readonly List<Job> _jobs = [];

    /// <summary>
    /// Bodies a character gave up on reaching, and until when (tick).
    /// </summary>
    readonly List<(CharacterId Attacker, CharacterId Victim, long Until)> _gaveUp = [];

    /// <summary>
    /// The AI tab's Execute Strangers option: the job also goes after strangers. Set in the serial input step; the interface reads it.
    /// </summary>
    volatile bool _strangers;

    /// <summary>
    /// Set while the walk hands over to the swing, so the walk's end does not drop the job.
    /// </summary>
    bool _switching;

    /// <summary>
    /// One execution.
    /// </summary>
    sealed class Job
    {
        public CharacterId Attacker;
        public CharacterId Victim;
        public bool Chopping;
        public float Time;
        public int Blows;
        public int Walks;
        public Vector2 Goal;
    }

    ExecutionSystem(CombatSystem? combat, CrimeSystem? crimes, CaptiveSystem? captives, DialogueSystem? dialogue)
    {
        _combat = combat;
        _crimes = crimes;
        _captives = captives;
        _dialogue = dialogue;
    }

    /// <summary>
    /// The system of the world made last (the running game's), for the AI tab.
    /// </summary>
    public static ExecutionSystem? Current { get; private set; }

    /// <summary>
    /// The AI tab's Execute Strangers option.
    /// </summary>
    public bool Strangers => _strangers;

    /// <summary>
    /// The factory the mod registers: finds the standard systems it works with.
    /// </summary>
    public static ITickSystem Create(SystemBuildContext context)
    {
        var made = new ExecutionSystem(context.Find<CombatSystem>(), context.Find<CrimeSystem>(), context.Find<CaptiveSystem>(),
            context.Find<DialogueSystem>());
        Current = made;
        return made;
    }

    /// <summary>
    /// Saves the Execute Strangers option with the game (one byte: 1 on, 0 off).
    /// </summary>
    public void Write(Stream stream) => stream.WriteByte(_strangers ? (byte)1 : (byte)0);

    /// <summary>
    /// Reads the Execute Strangers option back (a game saved without it keeps the default, off).
    /// </summary>
    public void Read(Stream stream) => _strangers = stream.ReadByte() == 1;

    /// <summary>
    /// Registers the row's act-out with this system.
    /// </summary>
    public void RegisterTasks(TaskHandlers tasks) => tasks.Register(Row, this);

    /// <summary>
    /// Registers this system as the Executing job's contributor.
    /// </summary>
    public void RegisterSources(DecisionSystem decisions) => decisions.RegisterJob(Row, this);

    #endregion

    #region Who may execute whom

    /// <summary>
    /// A body that can be executed: alive, out cold or playing dead, not carried, not in a cage or bed.
    /// </summary>
    static bool Executable(CharacterCold? v) =>
        v is { Medical: { Dead: false } m, Race: not null, Stats: not null } && (m.Unconscious || m.PlayingDead)
        && v.CarriedBy.IsNone && !v.IsImprisoned;

    /// <summary>
    /// A character that can carry out an execution: the player's, awake, able to fight, hands free, not carried or caged.
    /// </summary>
    static bool CanExecute(CharacterCold? a) =>
        a is { IsPlayer: true, Medical.Incapacitated: false, Fighter.CanAttack: true, Stats: not null, Race: not null }
        && a.Carrying.IsNone && a.CarriedBy.IsNone && !a.IsImprisoned;

    /// <summary>
    /// Whether the Executing job may take this body: never the player's faction or an ally of it; a hostile always; a stranger only when the
    /// setting allows strangers. Hostile means evidence the character is against the player: its squad fought a member of the executioner's
    /// squad (it tagged one TEMP_ENEMY), or, not an animal, its faction is at war with the player's (<see cref="CharacterFacts.RegardsEnemy"/>).
    /// Neither a lawless faction nor the BANDIT class is enough (seen in play in The Hub: slave hunters knocked out a crowd of Holy Nation Outlaws,
    /// a lawless faction of mostly civilians; taking their one bandit-class member turned the faction hostile, after which every downed member
    /// counted).
    /// </summary>
    bool JobMayTake(World world, int slot, CharacterCold v)
    {
        int player = world.Player.Faction;
        var relations = _crimes?.Relations;
        bool known = relations is not null && player >= 0 && relations.Contains(v.Faction);
        if (v.IsPlayer || v.Faction == player) return false;
        if (known && relations!.IsAlly(player, v.Faction)) return false;
        if (_strangers || FoughtSquad(world, slot, v)) return true;
        return !CharacterFacts.Animal(v) && known && CharacterFacts.RegardsEnemy(relations, player, v.Faction);
    }

    /// <summary>
    /// The body's squad took the executioner or one of its squad mates for an enemy (the short-term TEMP_ENEMY tag a fight leaves).
    /// </summary>
    bool FoughtSquad(World world, int slot, CharacterCold v)
    {
        if (_dialogue is null || v.SquadId < 0) return false;
        var table = world.Characters;
        var self = table.IdOf(slot);
        if (_dialogue.Memory.HasShort(world, v.SquadId, self, MemoryTag.TempEnemy)) return true;
        if (world.Squads.Find(table.Cold(slot)!.SquadId) is not { } squad) return false;
        foreach (var mate in squad.Members)
            if (mate != self && _dialogue.Memory.HasShort(world, v.SquadId, mate, MemoryTag.TempEnemy)) return true;
        return false;
    }

    #endregion

    #region Orders

    /// <summary>
    /// Takes the tick's execute orders and Execute Strangers changes.
    /// </summary>
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SetExecuteStrangers set) _strangers = set.On;
            if (command is not ExecuteOrder order) continue;
            if (order.AsJob) AddJobs(world, order);
            else
                Begin(world, order);
        }
    }

    /// <summary>
    /// Shift + Execute: each player character named gets the Executing job at the end of its list (once), and its jobs switch turns on.
    /// </summary>
    static void AddJobs(World world, ExecuteOrder order)
    {
        var table = world.Characters;
        foreach (var id in order.Attackers)
        {
            if (!table.TryResolveNext(id, out int a) || table.Cold(a) is not { IsPlayer: true } c) continue;
            if (c.Jobs.Exists(j => j.Type == Row)) continue;
            c.Jobs.Add(PermaJob.Untargeted(Row));
            c.Orders |= CharacterOrders.Jobs;
            DecisionSystem.Request(c);
        }
    }

    /// <summary>
    /// Gives the order to the nearest of its attackers that can run it.
    /// </summary>
    void Begin(World world, ExecuteOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Target, out int v) || !Executable(table.Cold(v))) return;

        var entry = new OrderEntry(Row, this) { Target = order.Target };
        var at = table.Next[v].Position;
        int best = -1;
        float bestDistance = float.MaxValue;
        foreach (var id in order.Attackers)
        {
            if (!table.TryResolveNext(id, out int a) || !PlayerOrders.CanRun(world, a, entry).CanRun) continue;
            float d = Vector3.DistanceSquared(table.Next[a].Position, at);
            if (d >= bestDistance) continue;
            (best, bestDistance) = (a, d);
        }
        if (best >= 0) PlayerOrders.Give(world, best, entry, order.Queued);
    }

    /// <summary>
    /// The order runs while the victim can still be executed (else it leaves the queue) and the executioner is fit (else it waits).
    /// </summary>
    OrderCheck IOrderOwner.CanRun(World world, int slot, OrderEntry order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Target, out int v) || !Executable(table.Cold(v))) return OrderCheck.Gone;
        return CanExecute(table.Cold(slot)) ? OrderCheck.Run : OrderCheck.Blocked();
    }

    /// <summary>
    /// Starts the walk to the body.
    /// </summary>
    void IOrderOwner.Start(World world, int slot, OrderEntry order) =>
        TaskStart.Start(world, slot, new TaskRequest(CharacterTask.GoTo, Row, TaskSource.Order) { Target = order.Target });

    /// <summary>
    /// A running execution stops when its body can no longer be executed.
    /// </summary>
    bool IOrderOwner.StillHolds(World world, int slot, OrderEntry order) =>
        world.Characters.TryResolveNext(order.Target, out int v) && Executable(world.Characters.Cold(v));

    /// <summary>
    /// Never saved: vanilla saves stay as they are, and a loaded game has no execution under way.
    /// </summary>
    int IOrderOwner.SaveKind(OrderEntry order) => -1;

    #endregion

    #region The Executing job

    /// <summary>
    /// The job runs while its execution is under way; otherwise it can start when the character is fit and a body it may take lies within
    /// <see cref="JobRange"/>.
    /// </summary>
    DecisionCheck IJobSource.CanRun(World world, int slot, int k)
    {
        var table = world.Characters;
        var cold = table.Cold(slot);
        var self = table.IdOf(slot);
        if (cold?.Running is { Row: Row, Source: TaskSource.Job } && _jobs.Exists(j => j.Attacker == self)) return DecisionCheck.Runs;
        if (!CanExecute(cold)) return DecisionCheck.No(null);
        return FindBody(world, slot) >= 0 ? DecisionCheck.Run : DecisionCheck.No(null);
    }

    /// <summary>
    /// Starts the walk to the nearest body the job may take.
    /// </summary>
    void IJobSource.Start(World world, int slot, int k)
    {
        int v = FindBody(world, slot);
        if (v < 0) return;
        var request = new TaskRequest(CharacterTask.GoTo, Row, TaskSource.Job) { Target = world.Characters.IdOf(v), Job = k };
        TaskStart.Start(world, slot, request);
    }

    /// <summary>
    /// The job can no longer run and nothing else can: its execution ends.
    /// </summary>
    void IJobSource.Stop(World world, int slot, int k) => Finish(world, slot);

    /// <summary>
    /// The nearest body within <see cref="JobRange"/> that can be executed, that the job may take, that no other execution has claimed and that
    /// the character has not given up on lately; -1 when none.
    /// </summary>
    int FindBody(World world, int slot)
    {
        var table = world.Characters;
        var self = table.IdOf(slot);
        var at = table.Next[slot].Position;
        int best = -1;
        float bestDistance = JobRange * JobRange;
        for (int v = 0; v < table.HighWater; v++)
        {
            if (v == slot || !table.Next[v].Alive) continue;
            float d = Vector3.DistanceSquared(table.Next[v].Position, at);
            if (d >= bestDistance || table.Cold(v) is not { } vc || !Executable(vc)) continue;
            var id = table.IdOf(v);
            if (Claimed(self, id) || GaveUp(world, self, id) || !JobMayTake(world, slot, vc)) continue;
            (best, bestDistance) = (v, d);
        }
        return best;
    }

    /// <summary>
    /// Another character's execution is already on this body.
    /// </summary>
    bool Claimed(CharacterId self, CharacterId victim) => _jobs.Exists(j => j.Victim == victim && j.Attacker != self);

    /// <summary>
    /// The character gave up on reaching this body within the last <see cref="GiveUpSeconds"/>.
    /// </summary>
    bool GaveUp(World world, CharacterId self, CharacterId victim)
    {
        _gaveUp.RemoveAll(g => g.Until < world.Tick);
        return _gaveUp.Exists(g => g.Attacker == self && g.Victim == victim);
    }

    #endregion

    #region Task

    /// <summary>
    /// The walk's start makes the job and sets off; the swing's start stands the executioner still with the clip.
    /// </summary>
    void ITaskHandler.Start(World world, int slot, in TaskRequest request)
    {
        var table = world.Characters;
        var attacker = table.IdOf(slot);
        if (request.Task == CharacterTask.Idle)
        {
            MovementSystem.Stop(table, slot);
            table.Cold(slot)!.ActionClip = Clip;
            return;
        }

        _jobs.RemoveAll(j => j.Attacker == attacker);
        var job = new Job { Attacker = attacker, Victim = request.Target };
        _jobs.Add(job);
        if (table.TryResolveNext(request.Target, out int v)) Walk(world, slot, v, job);
    }

    /// <summary>
    /// Another order, Stop, or the end of the execution: the job goes and the clip with it (not while the walk hands over to the swing).
    /// </summary>
    void ITaskHandler.End(World world, int slot, CharacterTask task, TaskEnd reason)
    {
        if (_switching) return;
        Drop(world.Characters, slot);
    }

    /// <summary>
    /// The task is done once its job is gone.
    /// </summary>
    bool ITaskHandler.Done(World world, int slot, CharacterTask task)
    {
        var attacker = world.Characters.IdOf(slot);
        return world.Characters.Next[slot].Task != (byte)task || !_jobs.Exists(j => j.Attacker == attacker);
    }

    /// <summary>
    /// Forgets the slot's job and clears its swing clip.
    /// </summary>
    void Drop(CharacterTable table, int slot)
    {
        var attacker = table.IdOf(slot);
        _jobs.RemoveAll(j => j.Attacker == attacker);
        if (table.Cold(slot) is { } c && c.ActionClip == Clip) c.ActionClip = null;
    }

    #endregion

    #region Each tick

    /// <summary>
    /// Advances every execution: the walk until in reach, then the swings until the victim dies.
    /// </summary>
    public void SlowWorld(World world)
    {
        for (int k = _jobs.Count - 1; k >= 0; k--)
        {
            if (k >= _jobs.Count) continue;
            Step(world, _jobs[k]);
        }
    }

    /// <summary>
    /// One tick of one execution.
    /// </summary>
    void Step(World world, Job job)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(job.Attacker, out int a))
        {
            _jobs.Remove(job);
            return;
        }
        if (!CanExecute(table.Cold(a)) || !table.TryResolveNext(job.Victim, out int v) || !Executable(table.Cold(v)))
        {
            Finish(world, a);
            return;
        }

        if (job.Chopping) Chop(world, job, a, v);
        else
            Approach(world, job, a, v);
    }

    /// <summary>
    /// The walk: on reaching the body the swing starts; a walk that ended short is tried again, then given up.
    /// </summary>
    void Approach(World world, Job job, int a, int v)
    {
        var table = world.Characters;
        ref var n = ref table.Next[a];
        var victim = table.Next[v].Position;
        if (InReach(n.Position, victim))
        {
            StartSwing(world, job, a);
            return;
        }

        bool walking = n.Task == (byte)CharacterTask.GoTo;
        bool moved = Vector2.Distance(job.Goal, Flat(victim)) > StandOff;
        if (walking && !moved) return;
        if (++job.Walks > MaxWalks)
        {
            world.Bark(job.Attacker, "Can't reach them.");
            _gaveUp.Add((job.Attacker, job.Victim, world.Tick + (long)(GiveUpSeconds / world.TickSeconds)));
            Finish(world, a);
            return;
        }
        Walk(world, a, v, job);
    }

    /// <summary>
    /// The walk hands over to the swing: the same order or job, now standing with the clip.
    /// </summary>
    void StartSwing(World world, Job job, int a)
    {
        var running = world.Characters.Cold(a)!.Running;
        var request = new TaskRequest(CharacterTask.Idle, Row, running?.Source ?? TaskSource.Order)
        {
            Target = job.Victim,
            Job = running?.Job ?? -1,
        };
        _switching = true;
        try
        {
            TaskStart.Start(world, a, request);
        }
        finally
        {
            _switching = false;
        }

        job.Chopping = true;
        job.Time = 0;
    }

    /// <summary>
    /// The swing: facing the body, one blow per clip at <see cref="BlowAt"/> of it, each booking the murder and counting with the victim's faction;
    /// the victim's death ends it. A body dragged out of reach ends it too.
    /// </summary>
    void Chop(World world, Job job, int a, int v)
    {
        var table = world.Characters;
        ref var n = ref table.Next[a];
        var victim = table.Next[v].Position;
        if (n.Task != (byte)CharacterTask.Idle || table.Cold(a)!.ActionClip != Clip || !InReach(n.Position, victim) || _combat is null)
        {
            Finish(world, a);
            return;
        }
        Face(ref n, victim);

        job.Time += world.TickSeconds;
        float swing = MathF.Max(_captives?.ChopSeconds ?? 1.5f, 0.1f);
        if (job.Time < (job.Blows + BlowAt) * swing) return;
        job.Blows++;

        var vc = table.Cold(v)!;
        _crimes?.SetPending(world, a, CrimeCode.Murder, vc.Faction, job.Victim, CrimeSystem.WitnessTimer);
        bool died = _combat.Execute(world, a, v, job.Blows);
        _combat.Deeds?.Blow(world, a, v, down: true, dead: died);
        if (died || job.Blows >= MaxBlows) Finish(world, a);
    }

    /// <summary>
    /// Ends the execution: the job goes and, while its task still runs, the task ends as done (an order leaves the queue; a job is decided again).
    /// </summary>
    void Finish(World world, int a)
    {
        var table = world.Characters;
        var cold = table.Cold(a)!;
        bool running = cold.Running is { Row: Row };
        Drop(table, a);
        if (running) TaskStart.End(world, a, TaskEnd.Done);
    }

    #endregion

    #region Geometry

    /// <summary>
    /// Sets off for a point beside the body, on its side facing the executioner, on the body's floor.
    /// </summary>
    static void Walk(World world, int a, int v, Job job)
    {
        var table = world.Characters;
        var from = table.Next[a].Position;
        var body = table.Next[v].Position;
        var away = Flat(from) - Flat(body);
        var dir = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : Vector2.UnitX;
        var goal = new Vector3(body.X + dir.X * StandOff, body.Y, body.Z + dir.Y * StandOff);
        MovementSystem.GoToFloor(world, a, goal, table.Cold(a)!.RunSpeed);
        job.Goal = Flat(body);
    }

    /// <summary>
    /// Within <see cref="Reach"/> across the ground and on the same floor.
    /// </summary>
    static bool InReach(Vector3 at, Vector3 body) =>
        Vector2.Distance(Flat(at), Flat(body)) <= Reach && MathF.Abs(at.Y - body.Y) <= MovementSystem.FloorSlack;

    /// <summary>
    /// The ground position (X/Z).
    /// </summary>
    static Vector2 Flat(Vector3 p) => new(p.X, p.Z);

    /// <summary>
    /// Turns the executioner to face the body.
    /// </summary>
    static void Face(ref CharacterHot n, Vector3 body)
    {
        float dx = body.X - n.Position.X, dz = body.Z - n.Position.Z;
        if (dx * dx + dz * dz > 1e-4f) n.Yaw = MathF.Atan2(dx, dz);
    }

    #endregion
}
