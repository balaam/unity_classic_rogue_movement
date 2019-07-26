# Rogue Movement Classic Unity

Post hackweek investigations into Roguelikes and ECS.

Movement is one of the first systems I want to look at. This project contains a demo roguelike that moves some creatures around a tile-based world.

## Optimisation

9034208 - *1.47ms* creature_movement for 150 creatures in an 80 * 25 world. Each time a creature moves checks every other creatures position in the world. 
a573258 - *0.08ms* creature_movement for 150 creatures in 80 * 25 world. Block data is tracked as it changes, creatures only need to do a look up.
494aad1 - *0.06ms* creature_movement for 150 creatures in 80 * 25 world. Changed Vector2Int to Coord (nothing) and stop allocating an array each loop - saves 0.02.
