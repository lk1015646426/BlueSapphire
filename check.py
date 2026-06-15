import re

with open('Models/CleanerScanItem.cs', 'r', encoding='utf-8') as f:
    text = f.read()

props = [
    'IsSelected', 'IsSelectableAndEnabled', 'Name', 'Description', 'RiskText', 'RiskSummary',
    'SizeText', 'CategoryText', 'ExecutionModeText', 'FileCountText', 'StatusHintText',
    'LockedByText', 'WhyItCanBeCleaned', 'OwnerApp', 'Path', 'ImpactAfterCleanup', 'HasLockOwnerInfo'
]

missing = []
for p in props:
    if p not in text:
        missing.append(p)

print('Missing CleanerScanItem properties:', missing)

try:
    with open('Models/CleanerExecutionEntry.cs', 'r', encoding='utf-8') as f:
        text = f.read()
except FileNotFoundError:
    text = ''
    print('CleanerExecutionEntry.cs not found')

props = [
    'ItemName', 'StatusText', 'SizeText', 'FailureSummary', 'ErrorMessage', 'RecoveryHint',
    'LockedByText', 'HasFailure', 'HasRecoveryHint', 'HasLockOwnerInfo', 'HasBackupPath', 'CanRestoreEntry',
    'CanDisableRule'
]

missing = []
for p in props:
    if p not in text:
        missing.append(p)

print('Missing CleanerExecutionEntry properties:', missing)

try:
    with open('Models/CleanerExclusionEntry.cs', 'r', encoding='utf-8') as f:
        text = f.read()
except FileNotFoundError:
    text = ''

props = ['DisplayName', 'Path']
missing = []
for p in props:
    if p not in text:
        missing.append(p)
print('Missing CleanerExclusionEntry properties:', missing)
