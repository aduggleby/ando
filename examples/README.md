# ANDO Examples

This directory contains example projects demonstrating ANDO usage.

## Examples

| Example | Description |
|---------|-------------|
| [0001-Simple](./0001-Simple) | Hello World - basic project build and publish |
| [0002-Library](./0002-Library) | Multi-project - console app with library dependency |

## Running Examples

### Single Example (Manual)

```bash
cd examples/0001-Simple
ando run build
./dist/HelloWorld
```

### All Examples (Automated)

The test runner builds ANDO, then runs each example in parallel Docker containers:

```bash
./examples/run-tests.sh
```

Options:
- `MAX_PARALLEL=8` - Control number of parallel workers (default: 4)

## Test Structure

Each example contains:
- `build.ando` - The ANDO build script
- `test.sh` - Verification script that:
  1. Runs `ando`
  2. Verifies expected outputs exist
  3. Runs the built executable and checks output

## Adding New Examples

1. Create a numbered directory: `0003-MyExample/`
2. Add a `build.ando` file
3. Add a `test.sh` script that verifies the build
4. Add a `README.md` documenting the example

The test runner automatically discovers all examples with `build.ando` and `test.sh`.
