# Execution

A code mod for [MeitouClient](https://meitou.kenshi.wiki) that lets your characters execute enemies who have been knocked unconscious, or cut off their arms and legs.

It only works in MeitouClient. The original Kenshi ignores it.

## What it does

- Right-click a knocked-out or play-dead character who is not in your squad and choose **Violence...**. A menu opens in its place: Execute, Left Arm, Right Arm, Both Arms, Left Leg, Right Leg, Both Legs and All Limbs. Limbs that are already gone are greyed out, and a prosthetic counts as gone (take it from their inventory instead).
- **Execute**: the nearest selected character walks over and chops at the body until it dies. Someone only playing dead won't lie still for it: they get up when the executioner reaches them.
- **A limb**: the same, but every swing goes at that limb, and the executioner stops the moment it comes off. Limbs come off this way even with the game's Dismemberment option set to Never; that setting still holds for every other hit. Losing a limb makes the victim bleed badly, so without first aid they can bleed to death.
- Hold Shift when you choose Violence... to give the selected characters the **Executing** job instead. Like any other job, they carry it out on their own when nothing more important is going on, finishing off downed bodies nearby after a fight.
- The swing depends on what the executioner holds: a big overhead chop with heavy weapons, hackers, blunt weapons, polearms and sabres, a downward combo with katanas, and a low kick bare-handed.
- Every swing is a real hit. How hard it lands depends on the executioner's weapon, skill and strength, and the victim's armor still counts. A weak character may need many swings, and the victim can wake up and crawl away before it is over.
- Executing someone is murder. Guards of law-abiding factions who see it put a bounty on you and come to arrest you. Witnesses whose faction is at war with the victim's don't care, and killing bandits is no crime. Every swing also counts against you with the victim's faction, just like hitting a downed body in a fight.
- Cutting off a limb is assault, with a smaller bounty. If the victim later bleeds to death, it becomes murder: at once if their faction already wants you for the assault, otherwise only if someone sees you.
- Executing and maiming give no skill experience.

## Squad AI option

In the squad overview's AI tab, beside Heal Allies and Rescue Allies:

- **Execute Neutrals**: off (the default), the Executing job only takes hostiles: factions at war with you and anyone who fought your squad. On, it also takes downed neutrals, everyone except your own faction and its allies. Their factions will turn against you, and guards who see it will call it murder. The option is saved with each game. The orders from the Violence... menu are never limited.

## Install

1. Download the zip from the Releases page and unzip it into Kenshi's `mods` folder, so that you have `Kenshi\mods\Execution\Execution.mod`, `Execution.dll` and `meitou.json`.
2. Turn it on in the MeitouClient launcher's mod list.

Or subscribe on the Steam Workshop and turn it on the same way.

The mod is built for MeitouClient 0.17.4 and newer (tested on 0.20.1). On an older version MeitouClient tells you it was built for a different version instead of loading it.

## Saves

Saves stay readable by the original game: the Executing job is stored under a key of the mod's own, and an execution in progress is not saved.

## Building

Build with the .NET 11 SDK against an official MeitouClient release: `dotnet build -c Release -p:MeitouBin=<release folder holding MeitouClient.dll>/`. A self-built MeitouClient won't do: its files carry a different version number, and a mod built against them won't load in the official releases.

## License

GNU General Public License, version 3 or (at your option) any later version, the same as MeitouClient. See [LICENSE](LICENSE).
