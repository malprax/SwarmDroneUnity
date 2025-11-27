# Swarm Drone Simulation - Clean Unity 6.2 Project

This is a clean Unity 6.2 (6000.2.13f1) project skeleton for your 2D swarm drone simulation.

## Scripts

- `Drone.cs` — basic random-movement drone with start/search/return/reset.
- `SearchTarget.cs` — renamed target trigger (NOT `Target.cs` to avoid conflicts).
- `SimManager.cs` — manages leader/member assignment, timer, object randomization.

## How to set up the scene

1. Open this folder as a Unity 6000.2.13f1 project.
2. Create a new 2D scene and save it as `Main` under `Assets/Scenes/`.
3. Add an empty GameObject called `SimManager` and attach `SimManager.cs`.
4. Create 3 drones:
   - Create an empty GameObject, add `SpriteRenderer`, `Rigidbody2D`, `CircleCollider2D`, and `Drone.cs`.
   - Duplicate it to get 3 drones and place them in the Home Base area.
   - Drag all three into `SimManager.drones` in the Inspector.
5. Create the search target:
   - Empty GameObject with a `SpriteRenderer` and a `BoxCollider2D` (IsTrigger checked).
   - Attach `SearchTarget.cs`.
   - Assign this object to `SimManager.target`.
6. Create 3 empty GameObjects as room spawn points and place them in each room.
   - Assign them to `SimManager.roomSpawnPoints`.
7. Add a Canvas with 2 TextMeshPro texts:
   - `TimerText` and `StatusText`.
   - Assign them to `SimManager.timerText` and `SimManager.statusText`.
8. Add 3 UI Buttons and hook them up:
   - Play   → `SimManager.Play()`
   - Reset  → `SimManager.ResetSim()`
   - Random → `SimManager.RandomizeObject()`

Now hit Play and the drones will search for the object, report who found it, and return home.
