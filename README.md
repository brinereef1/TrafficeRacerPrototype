# Traffic Racer

## Game Description

Traffic Racer is a 3D arcade-style racing prototype made with Unity. The player drives forward automatically on a multi-lane road, changes lanes to avoid AI traffic, and slows down smoothly on curves. The game includes generated/recycled road segments, roadside scenery, traffic spawning and lane-changing AI, a speed and distance HUD, engine and collision audio, and a Game Over screen with restart and exit options.

## Requirements

- **Unity:** Unity 6.3 LTS (6000.3.6f1 used for development)
- **Target build:** Windows standalone, x86_64
- **Controls:** Keyboard

## Controls

- **A** or **Left Arrow:** Move one lane left
- **D** or **Right Arrow:** Move one lane right
- **Start:** Begin gameplay
- **Restart:** Reload the scene and start gameplay without showing the Start menu
- **Exit:** Quit the standalone application (or stop Play Mode in the Unity Editor)

The car accelerates and moves forward automatically.

## Assets and Resources

- **Modular Lowpoly Track Roads FREE**: Road sections and fences.
- **Low Poly Vehicle Pack Lite**: Player and traffic vehicle models.
- **Pixabay**: Audio sound effects.
- **ChatGPT**: Generated UI graphics, including the title, menu buttons, and Game Over image.

Please check the applicable asset and audio licenses and include any attribution required by their terms.

## Open and Run the Project

1. Install Unity Hub and Unity Editor **6.3 LTS**.
2. In Unity Hub, choose **Add/Open** and select the project folder.
3. Open the project using Unity 6.3 LTS. Allow Unity to finish importing assets and compiling scripts.
4. In the Project window, locate and open **SampleScene**.
5. Check the Inspector references on the main systems:
   - **Player GameObject:** `PlayerController` should reference the `RoadManager` GameObject.
   - **RoadManager GameObject:** `RoadManager` should have its road segment definitions/prefabs assigned.
   - **TrafficSpawner GameObject:** `TrafficSpawner` should reference the `RoadManager`, include the traffic vehicle prefabs, and reference the player controller if not found automatically.
6. Press **Play**. The Start menu should appear. Select **Start** to begin.

The project uses Unity's legacy keyboard input API (`Input.GetKeyDown`). If keyboard input does not work, check **Edit → Project Settings → Player → Other Settings → Active Input Handling** and set it to **Input Manager (Old)** or **Both**.

## Important Inspector Review Test

The assignment asks reviewers to change the lane count, traffic spawn interval, and car speed in the Inspector without editing code. Stop Play Mode before changing these values, then press Play to test each configuration.

### Test 1: Change the number of lanes

1. Select the **RoadManager** GameObject.
2. In the `RoadManager` component, find **Lane Count** under **Lanes**.
3. Change it from **3** to **4**.
4. Press Play and check that the player can switch among the available lanes, traffic can spawn in the available lanes, and AI lane changes stay within the lane range.
5. Stop Play Mode and restore **Lane Count = 3** after the test.

### Test 2: Change traffic spawn frequency

1. Select the **TrafficSpawner** GameObject.
2. In the `TrafficSpawner` component, find **Min Spawn Interval** and **Max Spawn Interval** under **Spawning**.
3. Change them to **1.0** and **1.5** seconds, respectively.
4. Press Play and observe that traffic attempts to spawn more frequently. The safety checks may skip a spawn when no safe lane or gap is available; this is intentional.
5. Stop Play Mode and restore the original values (**1.5** and **2.5** seconds).

### Test 3: Change the car speeds

1. Select the **Player** GameObject and find the `PlayerController` component.
2. Change **Forward Speed** from **20** to **25**.
3. Select the **TrafficSpawner** GameObject. Under **Traffic Speeds**, change **Min Traffic Speed** to **12** and **Max Traffic Speed** to **28**.
4. Press Play. The player should accelerate toward the new forward speed, still slow down on curves, and newly spawned traffic should use the updated speed range.
5. Stop Play Mode and restore **Forward Speed = 20**, **Min Traffic Speed = 12**, and **Max Traffic Speed = 24**.

These are test values, not permanent gameplay requirements. Adjust them if vehicle dimensions or the desired difficulty make a configuration unsuitable.

## Build for Windows

1. Open **File → Build Profiles**.
2. Select **Windows, Mac, Linux** and choose **Windows** with **x86_64** architecture.
3. Make sure **SampleScene** is included in the scene list.
4. If needed, switch to the Windows platform.
5. Choose **Build**, select an output folder, and wait for the build to finish.
6. Run the generated `.exe`. Keep the executable and its accompanying data folder together.

The assignment asks for a Windows build or another agreed playable build. A WebGL build is not required unless separately agreed. During development, the WebGL build was blocked by a Windows application-control policy preventing Unity's build pipeline from loading `Unity.Options.dll`; therefore, WebGL has not been verified.

## Known Issues

Sometimes Traffic Vehicles are spawning in very less distance which leads to collision.  
