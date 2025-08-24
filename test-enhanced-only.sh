#!/bin/bash

# Test script for enhanced functionality only
echo "Testing enhanced functionality..."

# Build just our enhanced test files
dotnet build -c Debug --verbosity quiet \
  --property:WarningsAsErrors="" \
  --property:WarningsNotAsErrors="xUnit1026" \
  --property:NoWarn="xUnit1026" \
  Brainarr.Tests/Services/Core/EnhancedLibraryAnalyzerTests.cs \
  2>/dev/null

# Run just our enhanced tests
echo "Running enhanced LibraryAnalyzer tests..."
dotnet test Brainarr.Tests/Brainarr.Tests.csproj \
  --filter "FullyQualifiedName~EnhancedLibraryAnalyzerTests" \
  --verbosity quiet \
  --no-build \
  2>/dev/null

echo "Running chaos monkey stress tests..."
dotnet test Brainarr.Tests/Brainarr.Tests.csproj \
  --filter "Category=ChaosMonkey" \
  --verbosity quiet \
  --no-build \
  2>/dev/null

echo "Enhanced tests completed!"