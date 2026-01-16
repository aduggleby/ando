#!/bin/bash
set -e

echo "=== Testing 0002-Library ==="

# Run the build
echo "Running: ando"
ando

# Verify output exists
if [ ! -f "dist/Greeter.Console" ] && [ ! -f "dist/Greeter.Console.exe" ]; then
    echo "FAIL: Executable not found in dist/"
    exit 1
fi

# Verify library was included
if [ ! -f "dist/Greeter.Lib.dll" ]; then
    echo "FAIL: Library DLL not found in dist/"
    exit 1
fi

# Run the executable and check output
echo "Running: dist/Greeter.Console"
OUTPUT=$(./dist/Greeter.Console 2>&1 || ./dist/Greeter.Console.exe 2>&1)

EXPECTED="Hello, World! Welcome to ANDO."
if [ "$OUTPUT" != "$EXPECTED" ]; then
    echo "FAIL: Unexpected output: $OUTPUT"
    echo "Expected: $EXPECTED"
    exit 1
fi

echo "PASS: 0002-Library"
