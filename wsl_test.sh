#!/bin/bash
cd /mnt/c/Respositories/PySpector/PySpector-main/PySpectorC#
echo 'exec("ls")' > /tmp/test_ast.py
echo 'eval("1+1")' >> /tmp/test_ast.py
dotnet run --project src/PySpector.Cli -c Release -- /tmp/test_ast.py --format json 2>&1 | grep -E '"rule_id"|issues found'
echo "---"
echo "Now Flask with AST:"
dotnet run --project src/PySpector.Cli -c Release -- source_test/flask --format json 2>&1 | grep -E '"rule_id"' | sort -u