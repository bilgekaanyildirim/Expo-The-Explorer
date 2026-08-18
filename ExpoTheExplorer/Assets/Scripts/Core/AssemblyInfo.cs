using System.Runtime.CompilerServices;

// Makes GameState's balance setters (internal since economy-plan.md Adım 1)
// reachable from exactly two assemblies and no others. This is what turns the
// root CLAUDE.md invariant "every piece of data has a single writer" from a
// comment into a compile error: Bootstrap, LivesSystem, UI and every future
// assembly can READ SoftMoney/Gems but cannot assign them, so the only way to
// move money is through ProgressionSystem's Wallet.
//
// Adding a name here is therefore a real architectural decision, not a build
// fix -- it hands a second writer the keys. If a new assembly needs to change a
// balance, it takes a Wallet instead.
[assembly: InternalsVisibleTo("ExpoTheExplorer.Systems.ProgressionSystem")]

// The test assembly is a deliberate exception: GameStateTests asserts that the
// setters publish on change and stay silent on an unchanged write, which is
// Core's own contract and cannot be tested through a wallet without testing the
// wallet instead.
[assembly: InternalsVisibleTo("ExpoTheExplorer.Tests.EditMode")]
