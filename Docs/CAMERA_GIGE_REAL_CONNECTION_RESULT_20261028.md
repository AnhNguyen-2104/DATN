# AML-IPC: Camera GigE real connection result (2026-10-28)
Status: Physical CAM1 and CAM2 both identified by Hikrobot MVS and verified to stream individually. Automatic MVS GigE device enumeration still returns zero. Simultaneous dual-camera stream NOT verified.
## Mappings
Ethernet 2 / Intel I350 MAC 78-D0-04-2B-02-3A / primary IP 169.254.113.20/24 -> MV-CS016-10GC / DA7403922 / camera 169.254.113.1/24 / MAC 34-BD-20-6E-8D-AB.
Ethernet 3 / Intel I350 MAC 78-D0-04-2B-02-3B / PC IP 192.168.50.10/24 -> MV-CE050-31GC / J95682667 / camera 192.168.50.2/24 / MAC 34-BD-20-05-B6-E4 / DeviceUserID CAM_COLOR / MVS manufacturer text GEV.
Ethernet 4 EtherCAT and primary internet/PLC NIC left unchanged.
## Discovery diagnostic
Both cameras send valid GVCP discovery responses on their dedicated network interface. Packet capture with Docs/diagnose_mvs_packets_20261028.py confirmed the Hikrobot native SDK sent UDP discovery requests and physical cameras replied, yet enumeration count remained zero. Tests disabling non-Hik and Hik GigE filters separately both produced SDK count 0; all changed bindings restored. Ethernet2 retains extra 169.254.29.19/16 alias despite attempts to remove; its primary 169.254.113.20/24 works.
## Working operator workaround
Launch MVS with process-local MVS runtime DLL path (script Docs/START_MVS_CAMERA_RUNTIME_20261028.cmd).
Right-click Ethernet 2 > Add Remote Camera > 169.254.113.1. Double-click device to connect, click Start Continuous Grab. Captured actual LIVE image ~65.87 fps, 1440x1080, 0 errors / 0 lost packets at sampled time. Evidence Docs/CAMERA_GIGE_PROOF_20261028/01_CAM1_LIVE_1440x1080.png.
Right-click Ethernet 3 > Add Remote Camera > 192.168.50.2. In a FRESH MVS process, adding camera2 FIRST, then double-click to connect and Start Continuous Grab succeeded: ~23 fps, 2592x1944, 0 errors / 0 lost packets at sampled time. Captured frame was black; lighting, exposure, lens and trigger settings not modified. Evidence Docs/CAMERA_GIGE_PROOF_20261028/02_CAM2_STREAM_2592x1944.png.
## Important remaining bug
When CAM1 was connected/streaming, the manually-added CAM2 disappeared from MVS device list and re-adding failed despite its GVCP response, including after CAM1 disconnected. Restarting MVS clean and connecting CAM2 first worked. Therefore MVS auto discovery, remote-entry persistence and simultaneous dual-camera connections remain UNVERIFIED. Do not state both live simultaneously.
No Windows driver reinstall, reboot, camera parameter changes, PLC/ADS/TwinCAT downloads or motion commands were performed. Windows NIC config baselines and MVS INI backup preserved under Docs. Do not replay successful steps or duplicate NIC/filter changes without reading checkpoints.
Next: diagnose MVS auto-enumeration and two-camera lifecycle separately. Close MVS (releasing exclusive camera connection) before using production WPF.
