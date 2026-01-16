#!/bin/bash
#
# ANDO Example Test Runner
# Runs all examples in parallel Docker containers
#
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
MAX_PARALLEL="${MAX_PARALLEL:-4}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "=== ANDO Example Test Runner ==="
echo ""

# Step 1: Build ANDO for linux-x64
echo -e "${YELLOW}Building ANDO for linux-x64...${NC}"
cd "$PROJECT_ROOT"

if [ ! -f "dist/linux-x64/ando" ]; then
    dotnet publish src/Ando/Ando.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained \
        -o dist/linux-x64 \
        /p:PublishSingleFile=true
fi

echo -e "${GREEN}ANDO built successfully${NC}"
echo ""

# Step 2: Build test Docker image
echo -e "${YELLOW}Building test Docker image...${NC}"
docker build -t ando-test -f "$SCRIPT_DIR/Dockerfile.test" "$PROJECT_ROOT"
echo -e "${GREEN}Docker image built${NC}"
echo ""

# Step 3: Discover examples
EXAMPLES=()
for dir in "$SCRIPT_DIR"/0*/; do
    if [ -f "$dir/build.csando" ] && [ -f "$dir/test.sh" ]; then
        EXAMPLES+=("$(basename "$dir")")
    fi
done

echo "Found ${#EXAMPLES[@]} examples to test: ${EXAMPLES[*]}"
echo ""

# Step 4: Run tests in parallel
echo -e "${YELLOW}Running tests in parallel (max $MAX_PARALLEL workers)...${NC}"
echo ""

PIDS=()
RESULTS=()
LOGS_DIR="$SCRIPT_DIR/.test-logs"
rm -rf "$LOGS_DIR"
mkdir -p "$LOGS_DIR"

run_test() {
    local example="$1"
    local log_file="$LOGS_DIR/$example.log"

    # Run in Docker with the example mounted
    docker run --rm \
        -v "$SCRIPT_DIR/$example:/workspace:rw" \
        -v "$PROJECT_ROOT/dist/linux-x64/ando:/usr/local/bin/ando:ro" \
        -w /workspace \
        ando-test \
        bash /workspace/test.sh \
        > "$log_file" 2>&1

    return $?
}

# Start tests in parallel, respecting max workers
running=0
for example in "${EXAMPLES[@]}"; do
    # Wait if we've hit max parallel
    while [ $running -ge $MAX_PARALLEL ]; do
        for i in "${!PIDS[@]}"; do
            if ! kill -0 "${PIDS[$i]}" 2>/dev/null; then
                wait "${PIDS[$i]}" && RESULTS[$i]=0 || RESULTS[$i]=1
                unset 'PIDS[$i]'
                ((running--))
            fi
        done
        sleep 0.1
    done

    echo "  Starting: $example"
    run_test "$example" &
    PIDS+=($!)
    ((running++))
done

# Wait for remaining tests
for i in "${!PIDS[@]}"; do
    wait "${PIDS[$i]}" && RESULTS[$i]=0 || RESULTS[$i]=1
done

echo ""

# Step 5: Report results
echo "=== Test Results ==="
echo ""

PASSED=0
FAILED=0

for i in "${!EXAMPLES[@]}"; do
    example="${EXAMPLES[$i]}"
    result="${RESULTS[$i]:-1}"

    if [ "$result" -eq 0 ]; then
        echo -e "  ${GREEN}✓ PASS${NC}: $example"
        ((PASSED++))
    else
        echo -e "  ${RED}✗ FAIL${NC}: $example"
        echo "    Log: $LOGS_DIR/$example.log"
        ((FAILED++))
    fi
done

echo ""
echo "=== Summary ==="
echo -e "  Passed: ${GREEN}$PASSED${NC}"
echo -e "  Failed: ${RED}$FAILED${NC}"
echo ""

if [ $FAILED -gt 0 ]; then
    echo -e "${RED}Some tests failed. Check logs in $LOGS_DIR${NC}"
    exit 1
fi

echo -e "${GREEN}All tests passed!${NC}"
