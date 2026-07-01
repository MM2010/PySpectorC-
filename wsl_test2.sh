#!/bin/bash
cd /mnt/c/Respositories/PySpector/PySpector-main/PySpectorC#
echo "=== Flask +AST ==="
dotnet run --project src/PySpector.Cli -c Release -- source_test/flask --format json 2>/dev/null | grep '"rule_id"' | sort | uniq -c
echo "=== Flask --no-ast ==="
dotnet run --project src/PySpector.Cli -c Release -- source_test/flask --format json --no-ast 2>/dev/null | grep '"rule_id"' | sort | uniq -c
echo "=== Requests +AST ==="
dotnet run --project src/PySpector.Cli -c Release -- source_test/requests --format json 2>/dev/null | grep '"rule_id"' | sort | uniq -c
echo "=== Requests --no-ast ==="
dotnet run --project src/PySpector.Cli -c Release -- source_test/requests --format json --no-ast 2>/dev/null | grep '"rule_id"' | sort | uniq -c