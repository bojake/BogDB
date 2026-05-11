#!/usr/bin/env bash
set -euo pipefail

version="${1:-0.1.0-alpha.1}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/.artifacts/nuget"
package_list="$root/eng/package-projects.txt"

rm -rf "$out"
mkdir -p "$out"

dotnet restore "$root/BogDb.slnx"
dotnet build "$root/BogDb.slnx" --configuration Release --no-restore
dotnet test "$root/BogDb.Tests/BogDb.Tests.csproj" --configuration Release --no-build --verbosity minimal

while IFS= read -r project || [[ -n "$project" ]]; do
  [[ -z "$project" || "$project" =~ ^# ]] && continue
  dotnet pack "$root/$project" --configuration Release --no-build --output "$out" /p:PackageVersion="$version"
done < "$package_list"

echo
echo "NuGet packages written to: $out"
echo "Add as a local feed with:"
echo "  dotnet nuget add source \"$out\" -n BogDBLocal"
