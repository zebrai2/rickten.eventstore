# Package Icon

To add a professional package icon for NuGet:

## Option 1: Create Your Own Icon

1. Create a PNG image with dimensions **128x128 pixels** or larger
2. Save it as `icon.png` in the root directory
3. Update both `.csproj` files to include:

```xml
<PropertyGroup>
  <PackageIcon>icon.png</PackageIcon>
</PropertyGroup>

<ItemGroup>
  <None Include="..\icon.png" Pack="true" PackagePath="\" />
</ItemGroup>
```

## Option 2: Use a Simple Text-Based Icon

You can use a tool like [shields.io](https://shields.io) or create a simple icon with:
- Library initials "ES" (Event Store)
- Your brand colors
- Simple geometric shape

## Recommended Icon Specifications

- **Format**: PNG
- **Dimensions**: 128x128 px (minimum), 512x512 px (recommended)
- **Background**: Transparent or solid color
- **Style**: Simple, recognizable at small sizes
- **Content**: Avoid text, prefer symbols/shapes

## Design Ideas

- A stack of horizontal lines (representing events)
- A database cylinder with a lightning bolt
- Layered circles (representing snapshots/versions)
- An arrow pointing forward (representing event flow)

## Tools for Creating Icons

- [Canva](https://www.canva.com) - Free design tool
- [Figma](https://www.figma.com) - Professional design tool
- [GIMP](https://www.gimp.org) - Free image editor
- Adobe Illustrator/Photoshop - Professional tools
