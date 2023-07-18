# Custom Static Batching Utility

WARNING: This is a work in progress, probably contains innumerable bugs, and has only been tested in unity 2022. Use at your own risk. 

This is a complete replacement for unity's default static batching mesh generation process that seeks to improve mesh index data ordering to improve performance as well as add options for more advanced optimizations. The idea behind this is that the static batching system is capable of drawing multiple renderers in a single drawcall, if certain conditions are met. This happens when multiple renderers in the frame are contained within a continuous section of the static-batched combined mesh's index buffer and they share the exact same shader program, material, and UnityPerObject buffer. In order to maximize the chances of this happening, the sub-meshes in the combined mesh need to be sorted by shader, material and spatial locality.

## Pitfalls

Rendering multiple meshes in a single call with static batching is extremely fragile, and will fall apart in many cases. Any difference in the UnityPerObject constant buffer will forcibly split up a drawcall into multiple. This buffer contains things like lighting and reflection probe information which are guaranteed to be very variable, unless one is using Forward+ and APV. Differening Lightmap textures will also split the call, and which atlas objects end up in is seemingly random and not based on spatial locality. By far the worst though, is that renderers with multiple material slots will automatically force multiple calls. Each renderer can only specify an intitial submesh within the combined mesh and a number of submeshes following it that correspond to the material submeshes. This means it is impossible to sort the materials of a multi-material mesh, automatically breaking up the drawcalls into calls for each individual submesh. Always split meshes meant for static objects into multiple single material meshes to avoid this.

However, even in the most ideal case of a single material combined mesh that is entirely within the bounds of the view that and has completely uniform per-object data, unity will seemingly at random decide to split up rendering into multiple calls for no apparent reason. The sorted mesh combined mesh produced by this plugin can be significantly better than the unity default, but it will never result in anywhere close to the theoretical decrease in drawcall overhead it should be able to accomplish. The best solution as always to is to manually combine meshes and not give unity the opportunity to fail.

## Overview

It works by using using the IProcessScene callback to modify the temporary copy of the scene generated during the build/entring playmode process without permanently modifying the original scene file. The plugin perfoms the following steps:

- Find all static renderers in the scene and filter them for validity
- Generate an array of data about each renderer that effects static batching and sort including:
	- The number of materials used by the renderer
	- the shader of the first material's instanceID
	- the shader variant
	- the material's instanceID
	- the lightmap index
	- (TODO) the batching zone volume containing the renderer
	- the hilbert index of the renderer's bounds center
- Determine the vertex struct layout of all the meshes.
- Bin the sorted meshes into appropriately sized groups (currently assumes 16 bit index buffers, so bins containing >0xFFFF vertices)
- For each combined mesh, determine the union of the input meshes' vertex struct members and the minimumum dimension and format of each that can contain every mesh's data.
- Generate the index buffer for each combined mesh
- Copy and translate the vertex data of each renderer into their combined mesh
- Assign the combined mesh and submesh index and count to each renderer, and remove the static batching flag

# TODO

- Clean up the code and organize it better

- Add a batching zone component to give the user control over the spatial sorting rather than simply relying on a global hilbert index. Objects within each zone will calculate their hilbert index relative to the bounds of the zone rather than the bounds of the scene. Allow the user to specify the sorting order of the zones, or sort using a BVH?

- ~~Add support for 32-bit index buffer combined meshes and input meshes. How do we handle switching between 32-bit indices and 16 bit? Index buffers are enormous, 16-bit indices should be used whenever possible. Maybe just group 32-bit index input meshes into their own combined mesh? ~~ Done, 32-bit meshes are put into separate combined mesh objects to avoid inflating the index buffer size of 16-bit meshes

- ~~Add a settings asset to allow controlling the specifics of the mesh combining globally or per-scene, like specifying the data formats used for each member of the combined mesh's vertex struct or splitting the vertex buffer into separate position and everything else buffers.~~ Mostly done, no-per scene setting but there are now global settings in the preferences menu

- ~~Add support for split vertex buffers~~ Done, added support for 2 separate buffers for mobile, with options to set what attributes go into the second buffer in the settings

- Add an option to automatically split static renderers with multiple materials into multiple single material meshes and renderers