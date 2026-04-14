# Publishing to NuGet - Complete Guide

This guide walks you through publishing Rickten.EventStore to NuGet.org.

## Prerequisites

1. ✅ **NuGet Account**
   - Create account at [nuget.org](https://www.nuget.org)
   - Verify your email address

2. ✅ **API Key**
   - Go to [API Keys](https://www.nuget.org/account/apikeys)
   - Click "Create"
   - Name: `Rickten.EventStore`
   - Glob Pattern: `Rickten.EventStore.*`
   - Expiration: Choose appropriate duration
   - **Save the key securely** (you won't see it again)

3. ✅ **GitHub Repository** (for Source Link)
   - Create repository at `https://github.com/rickten/rickten.eventstore`
   - Push your code
   - Update repository URLs in `.csproj` files if different

## Step 1: Update Version Numbers

Update version in **both** `.csproj` files:

```xml
<Version>1.0.0</Version>
```

Follow [Semantic Versioning](https://semver.org/):
- **1.0.0** - Initial release
- **1.1.0** - New features (backwards compatible)
- **1.0.1** - Bug fixes
- **2.0.0** - Breaking changes

## Step 2: Update CHANGELOG.md

Document all changes in `CHANGELOG.md`:

```markdown
## [1.0.0] - 2025-01-15

### Added
- Initial release
- Event sourcing core abstractions
- Entity Framework implementation
...
```

## Step 3: Build in Release Mode

```bash
# Clean previous builds
dotnet clean --configuration Release

# Restore packages
dotnet restore

# Build in Release mode
dotnet build --configuration Release
```

## Step 4: Run Tests

```bash
# Run all tests
dotnet test --configuration Release

# Ensure all tests pass!
```

## Step 5: Pack the NuGet Packages

```bash
# Pack both projects
dotnet pack --configuration Release --output ./nupkg
```

This creates:
- `nupkg/Rickten.EventStore.1.0.0.nupkg`
- `nupkg/Rickten.EventStore.1.0.0.snupkg` (symbols)
- `nupkg/Rickten.EventStore.EntityFramework.1.0.0.nupkg`
- `nupkg/Rickten.EventStore.EntityFramework.1.0.0.snupkg` (symbols)

## Step 6: Test Package Locally (Optional but Recommended)

```bash
# Create a test project
mkdir test-package
cd test-package
dotnet new console

# Add local package source
dotnet nuget add source E:\Rickten\Rickten\nupkg --name LocalPackages

# Install your package
dotnet add package Rickten.EventStore.EntityFramework --version 1.0.0

# Test it works
# ... create some test code ...
```

## Step 7: Publish to NuGet.org

### Option A: Using .NET CLI (Recommended)

```bash
# Set your API key (one-time setup)
dotnet nuget push nupkg/Rickten.EventStore.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json

dotnet nuget push nupkg/Rickten.EventStore.EntityFramework.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

### Option B: Using GitHub Actions (Automated)

1. Add NuGet API key to GitHub Secrets:
   - Go to repository **Settings** → **Secrets and variables** → **Actions**
   - Click **New repository secret**
   - Name: `NUGET_API_KEY`
   - Value: Your NuGet API key

2. Create a GitHub Release:
   - Go to **Releases** → **Draft a new release**
   - Tag: `v1.0.0`
   - Title: `v1.0.0 - Initial Release`
   - Description: Copy from CHANGELOG.md
   - Click **Publish release**

3. GitHub Actions will automatically:
   - Build the project
   - Run tests
   - Create NuGet packages
   - Publish to NuGet.org

## Step 8: Verify Publication

1. **Check NuGet.org**
   - Go to https://www.nuget.org/packages/Rickten.EventStore
   - Go to https://www.nuget.org/packages/Rickten.EventStore.EntityFramework
   - Packages appear within 10-15 minutes

2. **Test Installation**
   ```bash
   dotnet new console -n TestInstall
   cd TestInstall
   dotnet add package Rickten.EventStore.EntityFramework
   ```

## Step 9: Post-Publication Tasks

1. ✅ **Tag the release in Git**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. ✅ **Update README badges** (optional)
   ```markdown
   [![NuGet](https://img.shields.io/nuget/v/Rickten.EventStore.svg)](https://www.nuget.org/packages/Rickten.EventStore/)
   [![Downloads](https://img.shields.io/nuget/dt/Rickten.EventStore.svg)](https://www.nuget.org/packages/Rickten.EventStore/)
   ```

3. ✅ **Announce the release**
   - Twitter/X
   - LinkedIn
   - Reddit (r/dotnet, r/csharp)
   - Dev.to blog post

## Troubleshooting

### Package Validation Errors

If you get validation errors:
```bash
# Enable detailed output
dotnet pack --configuration Release -v detailed
```

Common issues:
- Missing LICENSE file
- Missing README.md
- Invalid version format
- Dependency version conflicts

### "Package already exists"

If republishing the same version:
```bash
# Increment version in .csproj files
<Version>1.0.1</Version>

# Or use pre-release suffix for testing
<Version>1.0.0-beta1</Version>
```

### Symbols Package Upload Fails

Symbols are optional. You can publish without them:
```xml
<IncludeSymbols>false</IncludeSymbols>
```

## Publishing Checklist

Before publishing, ensure:

- [ ] Version number updated in both `.csproj` files
- [ ] CHANGELOG.md updated with release notes
- [ ] All tests passing (`dotnet test`)
- [ ] README.md is up to date
- [ ] LICENSE file exists
- [ ] Repository URL is correct in `.csproj`
- [ ] No placeholder values (e.g., "your-email@example.com")
- [ ] Documentation is complete
- [ ] Tested package locally
- [ ] NuGet API key is ready
- [ ] GitHub repository is public (for Source Link)

## Future Releases

For subsequent releases:

1. Update version number (follow SemVer)
2. Update CHANGELOG.md
3. Run tests
4. Pack and publish
5. Create GitHub release
6. Tag the commit

## Need Help?

- [NuGet Documentation](https://docs.microsoft.com/en-us/nuget/)
- [Semantic Versioning](https://semver.org/)
- [NuGet Package Guidelines](https://docs.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)

---

Good luck with your first publish! 🚀
