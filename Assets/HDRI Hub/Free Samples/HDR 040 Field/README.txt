HDRI Hub - HDR 040 Field (Free 8K)
==================================

Contents
- HDR_040_Field.hdr        8192 x 4096, Radiance HDR, equirectangular, unclipped
- HDR_040_Field_Bg.jpg     tonemapped 8K backplate / preview
- HDR_040_Field_Prev.jpg   small preview

How to use as a skybox in Unity
1. Select HDR_040_Field.hdr in the Project window.
2. In the Inspector set Texture Shape = Cube, Mapping = Latitude-Longitude Layout (Cylindrical), Max Size = 8192 (default), disable sRGB (Color Texture) and apply.
3. Create a Material with shader Skybox/Cubemap and assign the texture.
4. Window > Rendering > Lighting > Environment > Skybox Material: assign the material.
   For image based lighting set Environment Lighting Source = Skybox.

More free samples and 200+ commercial HDRI sets:
https://www.hdri-hub.com/shop/hdri

License: see Terms-of-Use.txt
