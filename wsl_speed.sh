#!/bin/bash
cd /mnt/c/Respositories/PySpector/PySpector-main/PySpectorC#

echo "=== Flask --no-ast ==="
for i in 1 2 3; do
  /usr/bin/time -f "%e" dotnet run --project src/PySpector.Cli -c Release -- source_test/flask --format json --no-ast > /dev/null 2>&1
done | awk '{sum+=$1; count++} END {printf "avg: %.1fs\n", sum/count}'

echo "=== Flask +AST ==="
for i in 1 2 3; do
  /usr/bin/time -f "%e" dotnet run --project src/PySpector.Cli -c Release -- source_test/flask --format json > /dev/null 2>&1
done | awk '{sum+=$1; count++} END {printf "avg: %.1fs\n", sum/count}'

echo "=== Requests --no-ast ==="
for i in 1 2 3; do
  /usr/bin/time -f "%e" dotnet run --project src/PySpector.Cli -c Release -- source_test/requests --format json --no-ast > /dev/null 2>&1
done | awk '{sum+=$1; count++} END {printf "avg: %.1fs\n", sum/count}'

echo "=== Requests +AST ==="
for i in 1 2 3; do
  /usr/bin/time -f "%e" dotnet run --project src/PySpector.Cli -c Release -- source_test/requests --format json > /dev/null 2>&1
done | awk '{sum+=$1; count++} END {printf "avg: %.1fs\n", sum/count}'