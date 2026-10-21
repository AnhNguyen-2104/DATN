# DATN ANGENT: offline WPF user-requested mode/layout patch (guarded, backup-first)
from pathlib import Path
from datetime import datetime
import hashlib, json, shutil, re, xml.etree.ElementTree as ET
root=Path(r"C:\Users\aml\orca\DATN")
src=root/"App"/"Vision"
files=["MainWindow.xaml","StationOverview.cs","Dashboard.cs","StationDashboards.cs"]
stamp=datetime.now().strftime("%Y%m%d-%H%M%S")
backup=root/"Docs"/("BACKUP_ANGENT_OVERVIEW_MODE_"+stamp)
before={}
for name in files:
    p=src/name
    before[name]=p.read_bytes()
    before[name].decode("utf-8")
# Guard against a concurrent edit after initial snapshot
def replace(name,old,new,n=1):
    t=working[name]
    if t.count(old)==0 and '\r\n' in t and '\n' in old:
        old=old.replace('\n','\r\n')
        new=new.replace('\n','\r\n')
    count=t.count(old)
    if count!=n: raise RuntimeError(f"Guard failed {name}: {old[:110]!r}, expected {n}, observed {count}")
    working[name]=t.replace(old,new)
working={name:data.decode("utf-8") for name,data in before.items()}
x="MainWindow.xaml"
replace(x,'<TextBox x:Name="OvBeltSetpointBox" Text="100" IsEnabled="False"','<TextBox x:Name="OvBeltSetpointBox" Text="100" IsEnabled="True"')
replace(x,'<Button x:Name="SysResetButton" Content="RESET" Click="DashGlobalCommand_Click" Tag="RESET" IsEnabled="False" MinWidth="58" MinHeight="25" Padding="8,1" FontSize="11" ToolTip="Fault acknowledge is not commissioned: no verified reset symbol exists in the PLC image."/>',
'''<Button x:Name="SysResetButton" Content="RESET" Click="DashGlobalCommand_Click" Tag="RESET" IsEnabled="False" MinWidth="58" MinHeight="25" Padding="8,1" FontSize="11" ToolTip="Fault acknowledge is not commissioned: no verified reset symbol exists in the PLC image."/>
                   <Button x:Name="OvVisionStartButton" Content="START VISION" Click="StationVisionStart_Click" MinWidth="88" MinHeight="25" Padding="6,1" FontSize="10" Margin="9,0,0,0" ToolTip="Starts camera/video detection on this PC only. Does not start conveyor or robots."/>
                   <Button x:Name="OvVisionStopButton" Content="STOP VISION" Click="StationVisionStop_Click" MinWidth="85" MinHeight="25" Padding="6,1" FontSize="10" ToolTip="Stops detection only; camera view remains live. NOT a machine stop."/>''')
replace(x,'<Button Grid.Column="3" Content="CONVEYOR PAGE" Click="DashOpenConveyor_Click" MinWidth="112" MinHeight="25" Padding="8,1" FontSize="11" VerticalAlignment="Center"/>',
'''<StackPanel Grid.Column="3" Orientation="Horizontal" VerticalAlignment="Center">
                   <CheckBox x:Name="OvManualModeToggle" Content="MANUAL MODE" Checked="OperatorManualModeChanged" Unchecked="OperatorManualModeChanged" FontSize="10" Foreground="{StaticResource ScadaAmber}" VerticalAlignment="Center" Margin="2,0,8,0" ToolTip="Default AUTO. Manual access requires explicit confirmation; it does not arm any PLC write."/>
                   <Button Content="ADVANCED / TAGS" Click="DashOpenConveyor_Click" MinWidth="118" MinHeight="25" Padding="8,1" FontSize="10" ToolTip="Read-only telemetry is available in all modes; legacy controls appear only after choosing Manual."/>
                 </StackPanel>''')
replace(x,'<TextBlock Grid.Column="2" Text="NO ADS COMMAND IS TRANSMITTED FROM THIS STRIP"','<TextBlock Grid.Column="2" Text="VISION COMMANDS: PC ONLY | MACHINE COMMANDS: NOT COMMISSIONED"')
replace(x,'<RowDefinition Height="*" MinHeight="200"/>\n            <RowDefinition Height="1*" MinHeight="160"/>',
'''<RowDefinition Height="2.6*" MinHeight="220"/>
            <RowDefinition Height="1*" MinHeight="130"/>''')
replace(x,'<RowDefinition Height="*" MinHeight="228" MaxHeight="360"/>\n            <RowDefinition Height="1*" MinHeight="196"/>',
'''<RowDefinition Height="2.6*" MinHeight="220"/>
            <RowDefinition Height="1*" MinHeight="130"/>''')
replace(x,'<ColumnDefinition Width="46*" MinWidth="380"/>\n              <ColumnDefinition Width="22*" MinWidth="190"/>\n              <ColumnDefinition Width="26*" MinWidth="220"/>',
'''<ColumnDefinition Width="64*" MinWidth="360"/>
              <ColumnDefinition Width="17*" MinWidth="175"/>
              <ColumnDefinition Width="19*" MinWidth="210"/>''',2)
replace(x,'<TextBlock Grid.Column="1" Text="READ-ONLY TELEMETRY" FontSize="9" FontWeight="Bold" Foreground="#27C9EC" VerticalAlignment="Center"/>',
'''<TextBlock Grid.Column="1" Text="MONITORING / MANUAL DETAILS" FontSize="9" FontWeight="Bold" Foreground="#27C9EC" VerticalAlignment="Center"/>''')
replace(x,'<Border Background="#112335" BorderBrush="#23384D" BorderThickness="1" CornerRadius="6" Padding="12">\n                <StackPanel>\n                  <TextBlock Text="CONVEYOR MOTOR CONTROLS',
'''<Border x:Name="ConveyorManualControlPanel" Visibility="Collapsed" Background="#112335" BorderBrush="#23384D" BorderThickness="1" CornerRadius="6" Padding="12">
                <StackPanel>
                  <TextBlock Text="CONVEYOR MOTOR CONTROLS''')
replace(x,'<TextBlock x:Name="DashControlNote" Text="HMI commands disarmed by default; a normal STOP does not replace the hardware emergency stop."',
'''<TextBlock x:Name="DashControlNote" Text="Manual access does not commission hardware; legacy Hz path requires separate operator authorization."''')
replace(x,'<Border Background="#112335" BorderBrush="#23384D" BorderThickness="1" CornerRadius="6" Padding="12" Margin="0,8,0,0">\n                <StackPanel>\n                  <TextBlock Text="STATION SYNCHRONIZATION"',
'''<Border Background="#112335" BorderBrush="#23384D" BorderThickness="1" CornerRadius="6" Padding="12" Margin="0,8,0,0">
                <StackPanel>
                  <TextBlock x:Name="ConveyorManualModeNote" Text="AUTO VIEW: read-only. Switch to MANUAL MODE from LINE OVERVIEW to show legacy controls (disarmed)." Foreground="#F4BB45" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,8"/>
                  <TextBlock Text="STATION SYNCHRONIZATION"''')
# Overview status text in data sink
x="StationDashboards.cs"
replace(x,'OvCvCommand.Text = _dashControlArmed ? "ARMED (SESSION)" : "DISARMED";',
'''OvCvCommand.Text = _operatorManualMode
            ? (_dashControlArmed ? "MANUAL / ARMED" : "MANUAL / DISARMED")
            : "AUTO / COMMANDS NOT COMMISSIONED";''')
# Mode handler with no hardware effects; entering/leaving always disarms.
x="StationOverview.cs"
anchor="    private void DashOpenGantry_Click(object sender, RoutedEventArgs e) =>"
new= '''    // Explicit mode selection controls visibility, never machine authority.
    // Any mode transition revokes the legacy conveyor arm; commissioning remains separate.
    private bool _operatorManualMode;

    private void OperatorManualModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool requested = OvManualModeToggle.IsChecked == true;
        if (requested && MessageBox.Show(
                "Open MANUAL maintenance controls? This does NOT arm the conveyor, " +
                "validate calibration, or authorize PLC/robot motion.\\n\\n" +
                "Normal machine operation remains on LINE OVERVIEW.",
                "DATN / Manual mode", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
        {
            OvManualModeToggle.IsChecked = false;
            return;
        }
        _operatorManualMode = requested;
        _dashControlArmed = false;
        DashControlArm.IsChecked = false;
        DashStartButton.IsEnabled = false;
        DashStopButton.IsEnabled = false;
        ConveyorManualControlPanel.Visibility = requested ? Visibility.Visible : Visibility.Collapsed;
        ConveyorManualModeNote.Text = requested
            ? "MANUAL MODE: legacy Hz controls are visible but disarmed; only trained operators may authorize use."
            : "AUTO VIEW: read-only. Switch to MANUAL MODE from LINE OVERVIEW for maintenance controls.";
        SensorHistory(requested ? "MANUAL MODE selected (no PLC command)" : "AUTO MODE selected (no PLC command)");
        UpdateControlSafetyBanner();
    }

'''
replace(x,anchor,new+anchor)
# Gate legacy start/stop paths before any ADS writes.
x="Dashboard.cs"
replace(x,'DashStopButton.IsEnabled=true;\n            DashStartButton.IsEnabled=_dashControlArmed',
'''DashStopButton.IsEnabled=_operatorManualMode && _dashControlArmed;
            DashStartButton.IsEnabled=_operatorManualMode && _dashControlArmed''')
replace(x,'''        if(DashControlArm.IsChecked==true)
        {
            var ans=MessageBox.Show(''',
'''        if(DashControlArm.IsChecked==true)
        {
            if (!_operatorManualMode)
            {
                _dashControlArmed = false;
                DashControlArm.IsChecked = false;
                DashControlNote.Text = "Manual conveyor controls are unavailable in AUTO mode.";
                return;
            }
            var ans=MessageBox.Show(''')
replace(x,'''        if(!_dashControlArmed || _dashStatus is null || !_dashStatus.EstopOk''',
'''        if(!_operatorManualMode || !_dashControlArmed || _dashStatus is null || !_dashStatus.EstopOk''')
replace(x,'''        if(_dashStatus is null||!_dashControlArmed)''','''        if(!_operatorManualMode || _dashStatus is null||!_dashControlArmed)''')
# Verify XAML parser before touching files.
ET.fromstring(working["MainWindow.xaml"])
# Save versioned full backups first. Restore on any failure.
backup.mkdir(parents=True,exist_ok=False)
manifest={}
for name,data in before.items():
    (backup/name).write_bytes(data)
    manifest[name]={"before_sha256":hashlib.sha256(data).hexdigest(),"after_sha256":hashlib.sha256(working[name].encode("utf-8")).hexdigest()}
(backup/"manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
for name,data in before.items():
    if (src/name).read_bytes()!=data:
        raise RuntimeError("Concurrent change detected; aborting without modifying: "+name)
done=[]
try:
    for name in files:
        (src/name).write_text(working[name],encoding="utf-8",newline="")
        done.append(name)
except Exception:
    for name in done: (src/name).write_bytes(before[name])
    raise
print("PATCH_OK",backup)
for name in files: print(name,manifest[name]["before_sha256"][:12],manifest[name]["after_sha256"][:12])
