from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(r"C:\Users\aml\orca\DATN\App\Vision")
ns="{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
x="{http://schemas.microsoft.com/winfx/2006/xaml}Name"
xml=ET.parse(root/"MainWindow.xaml").getroot()
by_name={n.attrib[x]:n for n in xml.iter() if x in n.attrib}
checks={
 "manual selector exists": "OvManualModeToggle" in by_name,
 "manual selector event": by_name["OvManualModeToggle"].get("Checked")=="OperatorManualModeChanged",
 "legacy conveyor panel hidden by default": by_name["ConveyorManualControlPanel"].get("Visibility")=="Collapsed",
 "global auto machine start not commissioned": by_name["SysStartButton"].get("IsEnabled")=="False",
 "mm/s proposal editable": by_name["OvBeltSetpointBox"].get("IsEnabled")=="True",
 "mm/s SET still locked": by_name["OvBeltSetButton"].get("IsEnabled")=="False",
 "mm/s belt START still locked": by_name["OvBeltStartButton"].get("IsEnabled")=="False",
 "vision start on overview": by_name["OvVisionStartButton"].get("Click")=="StationVisionStart_Click",
 "vision stop on overview": by_name["OvVisionStopButton"].get("Click")=="StationVisionStop_Click",
 "camera 01 shared live image preserved": by_name["Ov1CamImage"].get("Source") is not None,
 "station 01 camera preserved": by_name["Station1Preview"].get("Source") is not None,
}
tabs=[t for t in xml.iter(ns+"TabItem") if t.get("Header") in ("STATION 01","STATION 02")]
for tab in tabs:
    cols=[c.get("Width") for c in tab.iter(ns+"ColumnDefinition")]
    rows=[r.get("Height") for r in tab.iter(ns+"RowDefinition")]
    label=tab.get("Header")
    checks[label+" camera takes majority width"]="64*" in cols and "17*" in cols and "19*" in cols
    checks[label+" camera takes majority height"]="2.6*" in rows and "1*" in rows
code=(root/"Dashboard.cs").read_text(encoding="utf-8")
station=(root/"StationOverview.cs").read_text(encoding="utf-8")
checks["legacy start guarded by Manual"]='if(!_operatorManualMode || !_dashControlArmed' in code
checks["legacy stop guarded by Manual"]='if(!_operatorManualMode || _dashStatus is null||!_dashControlArmed)' in code
checks["operator consent required"]='MessageBoxButton.YesNo, MessageBoxImage.Warning' in station
checks["mode switch disarms"]='_dashControlArmed = false;' in station
checks["vision buttons reflect source availability"]='OvVisionStartButton.IsEnabled = HasActiveSource && !_trackingEnabled' in station
for name,passed in checks.items(): print(("PASS" if passed else "FAIL")+" "+name)
print(f"TOTAL={len(checks)} FAILURES={sum(not v for v in checks.values())}")
if not all(checks.values()): raise SystemExit(1)
