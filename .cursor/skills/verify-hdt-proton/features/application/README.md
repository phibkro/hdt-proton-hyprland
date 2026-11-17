# Original HDT application feature map

This map describes original HDT user journeys and the checks needed to migrate
those journeys. The [parity reference](../../../../../docs/reference/hdt-feature-parity.md)
owns native implementation status and source references. The
[existing native recipe](../native-avalonia.md) owns executable preview checks.

The original source baseline is HearthSim/Hearthstone-Deck-Tracker commit
`2698623dc0e91491671a614ae176f9db68831aa7`. Its correspondence to the installed
HDT 1.60.1 binary is unverified. This is a source-audited family map, not proof
that every upstream behavior, hidden dialog, or account-dependent path is covered.
Newly listed original journeys remain draft verification routes until driven.

| User goal | Feature file |
|---|---|
| Organize, select, and edit decks | [Deck management](decks.md) |
| Import or share a deck | [Import and export](sharing.md) |
| Track games and review results | [Matches and history](matches.md) |
| Draft and manage an Arena run | [Arena](arena.md) |
| Use Battlegrounds guidance | [Battlegrounds](battlegrounds.md) |
| Read and interact with tracking windows | [Overlay and auxiliary windows](windows.md) |
| Configure, update, and recover the tracker | [Preferences and lifecycle](preferences.md) |
| Use account and streaming services | [Online services](services.md) |

Read the parent verification skill before any drive. Use its launch, doctor,
evidence, and cleanup rules. Original WPF journeys require the private original
HDT and native CLR seed. Native preview journeys use a separate owned library,
preferences, and observations. Never reuse the operator's files as test fixtures.

Each feature file follows pstack's four-section format. A drive pairs an action
with an observable result. Existing executable routes are linked rather than
copied. Where no executable journey exists, the file says so explicitly; a
source reference or screenshot capture cannot pass that journey.

Only the coordinator drives an owned app instance. The user controls game entry,
picks, matches, account consent, and purchases. An unavailable prerequisite leaves
that route untested. A different mode's successful check cannot substitute for it.
