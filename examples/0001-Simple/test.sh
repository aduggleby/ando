#!/bin/bash
set -e

echo "=== Testing 0001-Simple ==="

# Run the build (--local since we're already in a container)
echo "Running: ando run build --local"
ando run build --local

# Verify output exists
if [ ! -f "dist/HelloWorld" ] && [ ! -f "dist/HelloWorld.exe" ]; then
    echo "FAIL: Executable not found in dist/"
    exit 1
fi

# Run the executable and check output
echo "Running: dist/HelloWorld"
OUTPUT=$(./dist/HelloWorld 2>&1 || ./dist/HelloWorld.exe 2>&1)

if [ "$OUTPUT" != "Hello, World!" ]; then
    echo "FAIL: Unexpected output: $OUTPUT"
    echo "Expected: Hello, World!"
    exit 1
fi

echo "PASS: 0001-Simple"
