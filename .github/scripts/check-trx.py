"""Print bounded TRX counters and reject missing, failed, empty or skipped test runs."""
from pathlib import Path
import os
import sys
import xml.etree.ElementTree as ET

def main():
    files = sorted(Path(sys.argv[1]).rglob('*.trx'))
    lines = ['| TRX | Total | Passed | Failed | Skipped |', '|---|---:|---:|---:|---:|']
    valid = bool(files)
    for path in files:
        root = ET.parse(path).getroot()
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        counters = root.find('t:ResultSummary/t:Counters', ns)
        if counters is None:
            raise ValueError('TRX counters missing')
        total, passed, failed, skipped = (int(counters.get(k, '0')) for k in ('total', 'passed', 'failed', 'notExecuted'))
        skipped_results = any(x.get('outcome') in ('NotExecuted', 'Skipped') for x in root.findall('.//t:UnitTestResult', ns))
        valid &= total > 0 and passed == total and failed == 0 and skipped == 0 and not skipped_results
        lines.append(f'| {path.name} | {total} | {passed} | {failed} | {skipped} |')
    report = '\n'.join(lines) + '\n'
    print(report)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as output:
            output.write(report)
    return 0 if valid else 1

if __name__ == '__main__':
    sys.exit(main())
