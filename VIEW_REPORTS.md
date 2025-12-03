# Quick Guide: How to View NBomber Reports

## 📊 Open the Latest HTML Report (RECOMMENDED)

### **Windows:**
```powershell
# Navigate to the latest report
cd E2E.PerformanceTests/reports
$latest = Get-ChildItem -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Start-Process "$($latest.FullName)\nbomber_report_*.html"
```

Or simply:
```powershell
# Quick one-liner
start E2E.PerformanceTests\reports\2025-12-03_04.37.75_session_54658288\nbomber_report_2025-12-03--04-39-05.html
```

### **Linux/Mac:**
```bash
# Find latest and open in default browser
xdg-open $(ls -t E2E.PerformanceTests/reports/*/nbomber_report_*.html | head -1)

# Or with Firefox
firefox $(ls -t E2E.PerformanceTests/reports/*/nbomber_report_*.html | head -1)
```

---

## 📈 What You'll See in the HTML Report

The HTML report contains interactive charts and tables:

### **1. Load Pattern Visualization**
Shows how requests ramped up and maintained over time:
```
Requests/Second
     │
100 ─┤        ┌─────────────────────────────────────┐
    │       ╱                                        │
 80 ─┤      ╱                                         │
    │     ╱                                          │
 60 ─┤    ╱                                           │
    │   ╱                                            │
 40 ─┤  ╱                                             │
    │ ╱                                              │
 20 ─┤╱                                               │
    │                                                │
  0 ─┴────────────────────────────────────────────────
      0s   10s   20s   30s   40s   50s   60s
      └─ramp─┘ └─────── sustained load ──────┘
```

### **2. Latency Distribution Chart**
Shows response time spread:
```
Percentile Distribution:
p50:  ████████████████░░░░░░░░░░░░░░░░░░  33.92ms
p75:  ████████████████████░░░░░░░░░░░░░░  40.22ms
p90:  ██████████████████████████░░░░░░░░  57.28ms
p95:  ████████████████████████████░░░░░░  57.28ms
p99:  ████████████████████████████████░░  87.94ms
max:  ████████████████████████████████████ 240.52ms
```

### **3. Success/Failure Breakdown**
```
Total: 5450 requests
┌──────────────────────────────────────┐
│ ✅ Success: 5450 (100%)              │
│ ❌ Failed:  0 (0%)                   │
└──────────────────────────────────────┘
```

### **4. Status Code Distribution**
```
Status Codes:
200 OK: ████████████████████████████████ 5450 (100%)
```

---

## 📝 View Reports in Terminal

### **Text Report (Quick View):**
```bash
cat E2E.PerformanceTests/reports/2025-12-03_04.37.75_session_54658288/nbomber_report_*.txt
```

Output:
```
test info
test suite: nbomber_default_test_suite_name
test name: nbomber_default_test_name
session id: 2025-12-03_04.37.75_session_54658288

scenario: trip_creation
  - ok count: 5450
  - fail count: 0
  - all data: 6.7 MB
  - duration: 00:01:00

load simulations:
  - ramping_inject, rate: 100, interval: 00:00:01, during: 00:00:10
  - inject, rate: 100, interval: 00:00:01, during: 00:00:50

+-------------------------+---------------------------------------------------------+
| step                    | ok stats                                                |
+-------------------------+---------------------------------------------------------+
| request count           | all = 5450, ok = 5450, RPS = 90.8                       |
| latency (ms)            | min = 15.75, mean = 36.95, max = 240.52, StdDev = 13.48 |
| latency percentile (ms) | p50 = 33.92, p75 = 40.22, p95 = 57.28, p99 = 87.94      |
+-------------------------+---------------------------------------------------------+
```

### **Markdown Report (GitHub/IDE):**
```bash
cat E2E.PerformanceTests/reports/2025-12-03_04.37.75_session_54658288/nbomber_report_*.md
```

---

## 📊 Import to Excel

### **Step 1: Open CSV**
```powershell
# Windows
start excel E2E.PerformanceTests/reports/2025-12-03_04.37.75_session_54658288/nbomber_report_*.csv
```

### **Step 2: Create Comparison**
If you have multiple test runs:

```bash
# Copy all CSV files to one folder
mkdir performance-comparison
cp E2E.PerformanceTests/reports/*/nbomber_report_*.csv performance-comparison/

# Now open all in Excel and create comparison charts
```

### **Key Columns to Track:**
- `ok_rps` - Throughput (requests/sec)
- `ok_mean` - Average latency
- `ok_95_percent` - p95 latency (SLA metric)
- `ok_99_percent` - p99 latency (tail latency)
- `failed` - Error count

---

## 📁 Report File Structure

```
E2E.PerformanceTests/
└── reports/
    └── 2025-12-03_04.37.75_session_54658288/
        ├── nbomber_report_2025-12-03--04-39-05.html  ← Open this in browser
        ├── nbomber_report_2025-12-03--04-39-05.md    ← GitHub/VS Code
        ├── nbomber_report_2025-12-03--04-39-05.txt   ← Terminal
        ├── nbomber_report_2025-12-03--04-39-05.csv   ← Excel
        └── nbomber-log-2025120311.txt                ← Debug logs
```

---

## 🎯 Key Metrics to Look For

### **Throughput**
```
✅ Good:  RPS = 90.8 (close to target 100 rps)
⚠️  Low:   RPS < 50 (underperforming)
❌ Bad:   RPS < 20 (serious issues)
```

### **Success Rate**
```
✅ Perfect: 100% success
⚠️  Good:    > 99.9% success
❌ Bad:     < 99% success
```

### **Latency (p95)**
```
✅ Excellent: < 50ms
✅ Good:      < 100ms
⚠️  Fair:     100-200ms
❌ Slow:      > 200ms
```

### **Latency (p99 - tail latency)**
```
✅ Excellent: < 100ms
✅ Good:      < 200ms
⚠️  Fair:     200-500ms
❌ Poor:      > 500ms
```

---

## 🔍 Troubleshooting Reports

### **No HTML report generated?**
Check the NBomber log:
```bash
cat E2E.PerformanceTests/reports/[SESSION_ID]/nbomber-log-*.txt
```

### **Can't find latest report?**
List all sessions:
```bash
ls -lt E2E.PerformanceTests/reports/
```

### **Want to clean old reports?**
Keep only last 5 sessions:
```bash
cd E2E.PerformanceTests/reports
ls -t | tail -n +6 | xargs rm -rf
```

---

## 📊 Example: Current Workload A Results

**Location:**
```
E2E.PerformanceTests/reports/2025-12-03_04.37.75_session_54658288/
```

**Performance Summary:**
- ✅ **100% success rate** (5450/5450 requests)
- ✅ **90.8 req/s** throughput
- ✅ **33.92ms p50** latency (median)
- ✅ **57.28ms p95** latency (SLA)
- ✅ **87.94ms p99** latency (tail)
- ✅ **240.52ms max** latency

**Verdict:** 🎉 Excellent performance! API handles rush hour traffic well.

---

## 🚀 Quick Commands Summary

```bash
# View HTML report (best)
start E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.html

# View in terminal (quick)
cat E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.txt

# Open in Excel (analysis)
excel E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.csv

# List all test sessions
ls E2E.PerformanceTests/reports/
```

Replace `[SESSION_ID]` with the actual session folder name like `2025-12-03_04.37.75_session_54658288`
