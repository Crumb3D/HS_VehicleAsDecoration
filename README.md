# HS Vehicle As Decoration

7 Days to Die 3.0–3.3. Same DLL on the client and the dedicated server.

These are the real bicycle, minibike, motorcycle, 4x4, and gyrocopter. They keep their wheels, suspension, and mod slots. Place one, open it with the service interaction, and install mods the same way as a normal vehicle. Armor, storage, lights, a plow, an extra seat, and the other mod meshes show on the model.

Drive, Ride, Refuel, and Take are turned off. There is no engine and no battery in the recipe. The inventory and creative icons are the real vehicle icons. Traders will not buy one.

The bicycle, minibike, and motorcycle are held upright, and the other shells cannot roll onto their side. Search the creative menu for "decoration". They are listed in the normal creative menu and in the prefab editor.

## Prefabs

A prefab file does not store vehicles. That is why a shell used to disappear when the prefab was loaded into a world. Turning off pickup does not keep a vehicle in the file.

From 1.0.3, placing a shell also places a hidden block in the cell under it. The prefab keeps that block. Loading the prefab into a testing world or a normal world spawns the shell again, mods included. Take stays off, so it cannot be picked up.

The prefab editor does not run block ticks. From 1.0.6 the shell is spawned as soon as you place it there, instead of waiting for a tick that never comes.

The world editor does not draw those live shells. It draws a preview from the blocks in the prefab file. From 1.0.7 each anchor block carries the vehicle mesh, so the preview shows the bicycle, minibike, motorcycle, 4x4, and gyrocopter. Save the prefab in the prefab editor before you place it in the world editor.

Shells placed before 1.0.3 were only vehicles. They are not in a prefab saved with an older copy. Place them again and save the prefab.

To remove one in the prefab editor, delete the invisible block in the cell under the vehicle.

## Craft

Same assembly list as the real vehicle, with the engine and battery left out. Wheel counts match the real vehicle. The bicycle crafts in your inventory. The others craft at a workbench.

| Shell | Wheels | Body | Left out |
| --- | --- | --- | --- |
| Bicycle | 2 | chassis, handlebars, 1 mechanical part | nothing else (the real bicycle has no engine) |
| Minibike | 2 | chassis, handlebars | engine, battery |
| Motorcycle | 2 | chassis, handlebars | engine, battery |
| 4x4 | 4 | chassis, accessories | engine, battery |
| Gyrocopter | 3 | chassis, accessories | engine, battery |
