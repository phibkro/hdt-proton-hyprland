# Native Arena selection phases

Status: bounded implementation; independently authored. Scope: native hero/card
view distinction and transitions, not game acquisition or full Arena parity.

## Goal and constraints

An Arena observation must tell the view whether it describes hero selection,
card drafting, an inactive menu, or an unavailable variant. Hero choices must not
look like zero-mana cards or inherit the previous offer's ratings. Main and
independent overlay use the same presentation boundary and consume observations
through the existing bounded Rust reader. No new acquisition, credentials,
network requests, assets or game input. Baseline and isolated process lifetimes
remain as defined in [native-ui-isolation.md](native-ui-isolation.md).

## Authoritative contract and ownership

[Arena.cs](../../avalonia/Hdt.Avalonia/Arena.cs) owns the versioned contract and
validation. Schema 2 uses a tagged selection union: hero selection owns typed
hero identities, card draft owns card offers, inactive owns no choices, and
unavailable owns an explicit variant/reason. Legacy top-level offers must be
empty in schema 2. Unknown or mixed choice shapes fail closed. Hero/inactive
phases cannot carry previous card picks. Nullable class and absent statistics
remain unknown; no identity, score or entitlement is inferred.

`ArenaV1Adapter` preserves schema 1's card-only/inactive meaning, including its
historical inactive picked-deck semantics. Zero picks, offer count, names and
hero-like card IDs never imply a hero phase. The native envelope remains schema 1
and embeds either validated Arena version with matching session/sequence/time.

The producer owns observed facts. Each UI owns its own reader, freshness and
projection; no state relay through Main is introduced. JSON member ordering is
irrelevant, including when Rust sorts object fields. The already bounded JSON
boundary converts unsupported/missing discriminator shapes to controlled input
failure. Existing session ordering, five-second native freshness, byte and
regular-file limits remain in force.

## Views and explicit gaps

[Shared presentation](../../avalonia/Hdt.Avalonia/OverlayRaster.cs) renders hero
identity/class with an explicit statistics-unavailable label; cards retain supplied
mana, nullable rating and synergy semantics. Every replacement first clears old
choice controls. Menu/unsupported phases remove advice; source faults clear facts
and recovery requires fresh valid state. Observation controls advance replay only;
they never select heroes/cards inside Hearthstone.

The optional source-only exporter uses existing public cached model collections
and explicit package/redraft state. Ambiguous collections and unsupported variants
produce unavailable. Public hero statistics lack offered hero identities, so that
bridge's hero view reports the gap instead of inventing choices. Hero-power,
dual-class, package and redraft assistance remain unavailable. Wine/HDT dispatcher
acquisition dependence, authenticated statistics, animation, artwork and exact
WPF pixel parity are unchanged gaps.

Reference evidence: read-only HDT revision
`2698623dc0e91491671a614ae176f9db68831aa7`, `ArenaPickHelper.xaml` separate
hero/card/hero-power regions and `ArenaPickHelperViewModel.UpdateChoices` clearing
old collections before replacement. No upstream implementation or assets copied;
see [license provenance](../../avalonia/README.md#provenance-and-licensing).

## Executable acceptance and falsifiers

Use [the native verification recipe](../../.cursor/skills/verify-hdt-proton/features/native-avalonia.md).
The existing rendered runner drives pointer input through hero → cards → new offer
→ unsupported → menu and rewind at 1120×760 and 800×600, retaining screenshots and
geometry checks. It verifies schema 1 compatibility, mixed/missing payload
rejection, the actual optional writer → Rust reader → native polling path,
malformed/stale clearing and fresh recovery. No fabricated ratings may appear.

`check-native-isolation.sh` additionally publishes the typed phases through real
sibling processes under private Bubblewrap/Xvfb, compares actual overlay pixels,
observes menu unmapping, and retains main/overlay screenshots. Existing STOP,
kill/restart, source-failure, focus, geometry, empty-input shape and exact-child
reaping regressions still run. The unmodified baseline private X11 journey and
intentional managed-failure cleanup control remain required. Clean-head source
identities and private run logs are evidence; headless success alone is not live
game acceptance or ordinary display-server acceptance.
