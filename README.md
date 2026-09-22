# Line of Fire (Ateş Hattı)

A 2D top-down arcade shooter built with Unity 6 and C#. Survive waves of enemies,
fight a boss every 45 seconds, and spend the gold you collect on permanent upgrades
that carry over between sessions.

**▶ Play in your browser: https://onurmdk.itch.io/line-of-fire**

---

## Features

- **Three enemy types** with distinct health, speed and reward values, spawned from
  separate pools with difficulty scaling over time
- **Boss encounter** every 45 seconds; boss health and damage scale with each cycle
- **Persistent upgrade shop** — Fire Rate, Damage and Max HP, each with level-based
  costs that grow geometrically
- **Save system** with versioned data, so save format changes don't corrupt existing
  progress
- **Mouse and touch input**, playable on desktop and mobile browsers

## Tech stack

| Area | Choice |
|---|---|
| Engine | Unity 6.3.13f1 (URP 2D) |
| Language | C# |
| Target | WebGL (Gzip + decompression fallback) |
| UI | Unity UI + TextMeshPro |
| Persistence | `PlayerPrefs` with JSON serialization |

---

## Architecture notes

These are the design decisions I'd want to talk through in a code review.

### Object pooling for all runtime spawns

Bullets, enemies and explosion particles are managed with `UnityEngine.Pool.ObjectPool<T>`
rather than `Instantiate`/`Destroy`. Pools are pre-warmed on `Awake` and disposed on
`OnDestroy`.

This matters most on WebGL, where garbage collection pauses are far more visible than
on desktop. `Bullet.ReturnToPool()` also guards against double-release: a bullet that
hits an enemy and crosses the screen boundary in the same frame would otherwise be
returned to the pool twice and later handed out to two callers at once.

### Centralized collision resolution

Individual colliders don't contain combat logic. A lightweight `CombatRelay` component
forwards `OnTriggerEnter2D` to a single `CombatManager`, which owns all damage rules in
one place.

### Damage through an interface, not a concrete type

`CombatManager` applies damage through `IDamageable` rather than fetching a concrete
`Enemy` component:

```csharp
IDamageable target = targetCollider.GetComponent<IDamageable>();
if (target == null || !target.IsAlive) return;

bool killed = target.TakeDamage(_bulletBaseDamage);
```

`Enemy` and `Boss` both implement the interface. Before this change the manager called
`GetComponent<Enemy>()`, which returned `null` for the boss — the boss could not be
damaged, never died, and the spawner's "wait for the boss to be defeated" check blocked
enemy spawning permanently. Depending on a capability instead of a concrete type removed
the whole class of bug.

### Batched save writes

Gold is awarded on every kill, and the save layer originally called `PlayerPrefs.Save()`
each time. On WebGL that call is a synchronous JavaScript interop plus an IndexedDB
flush — several times a second during normal play.

Writes are now marked dirty in memory and flushed on a 15-second timer, on pause, on
game over and on quit. The trade-off is explicit: a hard tab close can lose up to 15
seconds of progress, which is cheaper than a frame hitch on every kill.

### Event-driven UI

The HUD subscribes to `OnGoldChanged`, `OnHpChanged` and `OnEnemyKilled` rather than
polling manager state in `Update()`. Editor-only validation runs through
`[Conditional("UNITY_EDITOR")]` so it costs nothing in a release build.

---

## Notable fixes

Each of these was found by reading the code rather than by hitting the bug in the editor.

| Issue | Cause | Fix |
|---|---|---|
| Ship did not respond to the mouse in WebGL builds | Mouse handling was wrapped in `#if UNITY_EDITOR \|\| UNITY_STANDALONE`; neither symbol is defined for WebGL, so the block was stripped from the build and the bug was invisible in the editor | Removed the conditional compilation |
| Boss was invulnerable; enemy spawning stopped permanently after the first boss | `CombatManager` resolved targets as `Enemy`, which the boss prefab does not have | Introduced `IDamageable` (see above) |
| HUD displayed impossible values such as `HP 10/1` | Max HP was defined both in the inspector and as a constant in the shop; the shop overwrote it at startup, and the upgrade method never clamped current HP | Clamped current HP and aligned the two sources |
| Frame hitches during combat on WebGL | `PlayerPrefs.Save()` on every kill | Dirty-flag batching (see above) |
| Starfield disappeared the moment the game started | Particles were created without `remainingLifetime`, so they expired on the first simulation step; they were only visible while `Time.timeScale` was 0 | Assigned an explicit lifetime |

---

## Known limitations

Things I'd change next, in priority order:

1. **Score is time-based.** High score currently stores survival time. Per-enemy score
   values weighted by elapsed time would read better and make enemy variety meaningful.
   This requires a save migration, which is what the `saveVersion` field exists for.
2. **Upgrade values live in three places** — constants in `ShopManager`, serialized
   fields on prefabs, and scene objects. Moving them into `ScriptableObject` assets would
   give a single source of truth and let balance be tuned without touching code. Two of
   the bugs above came from exactly this duplication.
3. **Screen-bounds calculation is duplicated** across five classes, each polling for
   resolution changes in `Update()`. A single service that raises an event on change would
   remove both the duplication and the per-frame work.
4. **`Boss` and `BossBullet` still use `Instantiate`/`Destroy`**, inconsistent with the
   pooling used everywhere else.
5. **No audio.**

---

## Running locally

```bash
git clone https://github.com/onurmdk/atesHatti.git
```

Open the project in **Unity 6.3.13f1** and load `Assets/Scenes/SampleScene.unity`.

To produce a WebGL build that works on static hosts such as itch.io or GitHub Pages:

- **Publishing Settings** → Compression Format: **Gzip**, Decompression Fallback: **on**
  (static hosts don't send a `Content-Encoding` header; without the fallback the build
  hangs on a blank screen)
- **Resolution and Presentation** → Canvas 450 × 800, WebGL Template: **Minimal**
- **Other Settings** → Color Space: **Gamma**

A WebGL build cannot be opened directly from the filesystem. Serve it over HTTP:

```bash
cd <build-folder>
python3 -m http.server 8000
```

---

## Credits and development notes

Graduation project, Eastern Mediterranean University, 2026. Developed as a team of two:
I was responsible for the game implementation — all C# code, Unity scene and prefab
setup, the WebGL build and the release — while my teammate produced the project
documentation and written reports.

**AI assistance.** Parts of this codebase were written with the help of AI coding tools.
The architecture notes and fixes documented above reflect a subsequent review pass of the
code: each change is recorded here with its reasoning so the design can be discussed and
defended on its merits.
