# MR-J4-A / QJ71C24N ABS SCARA — execution checkpoint 2026-11-10

## Status: ĐÃ GHI NATIVE / ĐÃ BUILD NGOẠI TUYẾN / CHƯA THỬ PHẦN CỨNG

`ABS_Home [PRG] Program [ST]` and 40 local labels were populated in the existing native Robot_SCARA project using GX Works2 editor/paste and official CSV import. The project was saved with Ctrl+S. Final native **Rebuild All Completed.Error: 0, Warning: 0, CheckWarning: 0**. External reference-model checks passed; no native PLC simulation or hardware execution was performed.

## Actual native state and changes

- Existing GX Works2 process: `GD2.exe`, PID 28736, executable `C:\Program Files (x86)\MELSOFT\GPPW2\GD2.EXE`.
- Actual title: `MELSOFT Series GX Works2 E:\Orca\DATN\PLC\Mitsubishi\DATN_LINE\Robot_SCARA - [ABS_Home [PRG] Program [ST] ]`.
- Source was entered through the native ST editor and clipboard paste. Full native Ctrl+A/Ctrl+C readback matched `PLC/Mitsubishi/ABS_Home.st` exactly after newline normalization, including both dedicated instruction calls at the end. Final source SHA-256: `b768e667782d828c9b09d4b6ef9cc2fa5cf75b8b966fa657e02724c30f064e2a` (5765 UTF-8 bytes).
- Important compile finding: the previously unregistered POU was excluded from early zero-error builds. Registering it under **Standby Program → MAIN1 → ABS_Home** exposed real local-label errors. Reserved names `I`, `J`, `Sum` and datatype declarations were corrected; final registered POU rebuilt with zero errors/warnings/check warnings. Standby registration is deliberately not Scan/Initial registration; no execution request was added.
- Files added: `PLC/Mitsubishi/ABS_Home.st`, importable UTF-16LE tab-delimited `ABS_Home_labels.csv`, and original empty export `ABS_Home.csv`. Native save updated `checkout.xml`, `dataprotection.xml`, `history.xml`, `Project.gd2`, `projectdatalist.xml`, `user.xml`, `_hdb` through GX itself. No raw proprietary edits. No edit commands targeted POU_01; a semantic before/after POU_01 export comparison was not performed.
- No PLC connection, download/write, force, RUN/STOP, servo enable, motion, safety change, or live settings change performed.
- Saved storage SHA-256 after final rebuild/save: `_hdb` = `FAF1D570373324E7460A8D13232051A546354FEEC0DEB5DFC6AD1A0A168B5D0C`; `projectdatalist.xml` = `73F8F84D781C0BD672019F6AF837FBD2FC24E67FE92FD971E0E934544334ABEE`. Both differ from the original backup. Final screenshot: `C:\Users\aml\AppData\Local\Temp\omp-computer-15c88947583dc9ec.png`.

## Backup evidence

Backup of current ON-DISK project:
`E:\Orca\DATN\Docs\BACKUP_MRJ4A_ABS_SCARA_20261110_0755\Robot_SCARA`

Copy-Item could not copy `_dup` because GX holds it open; all other 11 files copied and SHA-256 matched the source. This preserves disk content, not any unsaved in-memory editor changes. Restoration/opening of the backup has NOT been tested. Do not overwrite the working project while GX holds it open.

| File | Bytes | SHA-256 source = backup |
|---|---:|---|
| checkout.xml | 9821 | 80AB945B4DA1E4A1A33C2406754E6BB79F3F86420DA8DB529A5B5155B8867F2E |
| dataprotection.xml | 53638 | C4FD159696F71600B3446D29E226627735BA01D27A9FC02A6C0D44550FA2C52D |
| history.xml | 28767 | 9391D32397EE92B28FD1F222FB020781A5B955CBF9C3A039AE9B5EA37FF8DF7F |
| label.xml | 372 | 38F3403C1B451ABE65BD78B6DDAD2C1386FBE0D275FF423B67AE9BF92E28938A |
| labellink.xml | 394 | C78A7BF3FF44BEE2943D24AFD87A6BCE82C636BB7B9025A5AF9AECFD243C32A7 |
| Project.gd2 | 0 | E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 |
| projectdatalist.xml | 30495 | 371BD54E3BA14A39E2B37E2167A43C200D904C803788C85725DD793AB4876906 |
| securitylevel.xml | 163840 | C5AF25C453A76F2128E8BC9E62DBDD902EED0BB748809AEB1BDAC2263231CCB5 |
| storedhistory.xml | 376 | 3DA86CE4992D04520A7362157853DF7D4F2082B05EBFD017A950CB676D21496B |
| user.xml | 192512 | D01A80FDF5F5AE8B1C8F1A6B8CFAE320EB1DF3039B4FB11469B25A1DA8802E65 |
| _hdb | 528384 | FE1486C8587BB077A984436FF3FAEDF5C8A6DB570FBA9353ACC9B78C31C3A3F2 |

`Project.gd2` is a zero-byte marker in this existing structured-storage project. Do not treat copying that marker alone as a project backup. No raw proprietary edits were attempted.

## Primary manual findings actually read

Manufacturer source: [MR-J4 General-Purpose Interface Instruction Manual SH030107V](https://dl.mitsubishielectric.com/dl/fa/document/manual/servo/sh030107/sh030107v.pdf). Downloaded successfully to temporary `C:\Users\aml\AppData\Local\Temp\mrj4a_sh030107v.pdf`.

- §12.8.1, printed p12-26: command 02, data No.91 reads absolute position in command-pulse units, returned as hexadecimal 32-bit length.
- §14.4.1(8), p14-17: data90 is motor-side pulse-unit ABS; data91 is command-unit ABS, eight data characters. Do not substitute data90.
- §14.2.1, p14-5: 8 data bits, even parity, one stop bit; baud includes 19200. PC21 baud selection 1 = 19200; its response-delay field is separate. PC20 station range 0..31. No live values were read or changed.
- §14.3.2, p14-7: station0 is one ASCII byte `30h`, not two station bytes. Request `SOH '0' '0' '2' STX '9' '1' ETX '0' '1'` is 10 bytes; station0 + command02.
- §14.3.4, p14-8: sum bytes AFTER initial SOH/STX through ETX inclusive; lower byte encoded as two ASCII hexadecimal digits. Request sum is 0x101, lower byte 01h. The required request bytes are consistent with this rule.
- §14.3.3, p14-8: response status A = positive/servo normal; a = positive processing with servo alarm; B..F / b..f are parity/checksum/character/command/data-number errors. Alarm status must remain visible, not be silently represented as healthy.
- §14.3.5, p14-9: if STX has not started within 300ms after transmission, send EOT, wait100ms, retransmit with a bounded sequence. §14.3.6 bounds retries for communication faults.
- §14.3.7, p14-9: wait at least3.5s after servo power-on before ordinary communication.
- §14, p14-1: RS-422 supported with amplifier software A3 or later; USB CN5 and RS-422 CN3 communication mutually exclusive.
- §12.8.2(2), p12-27: on receipt, current position is also set as a position command value inside the amplifier. Thus protocol classification as a reading command is NOT proof of zero internal amplifier-state effect. No query was transmitted in this execution.

QJ71C24N contract verified from [SH080006W](https://dl.mitsubishielectric.com/dl/fa/document/manual/plc/sh080006/sh080006w.pdf), §§3,4.5.2,6,10: module start0080 → U8; CH2 X87/X88 completion, X89 transmitting, X8A read request, X8B receive abnormal. CH2 current protocol610=6; current transmission611 baud07 in bits8..11 and 8E1 low bits0Eh. Source rejects incompatible byte-unit, delimiter, conversion, transparent-code, DC-code and user-frame settings rather than changing module parameters. Receive-clear uses BFM328 only after idle/drain checks. Structured ST `G_OUTPUT`/`G_INPUT` signatures verified in [SH080785ENGM](https://www.mitsubishielectric.com/dl/fa/document/manual/plc/sh080785eng/sh080785engm.pdf). INPUT allowable receive size is **words**, even with byte-unit data count; RxData is16 words, copies up to32 received bytes low-byte-first, and rejects oversized pending batches. Physical channel settings were not read online or changed.

## Offline verification status

| Required check | Actual result |
|---|---|
| Native ABS_Home source and local labels | WRITTEN; full source readback matched; 40 labels imported |
| Native GX Rebuild All, registered Standby POU | Error0 / Warning0 / CheckWarning0 |
| Valid response parser cases | External JS reference model: 0 and74565 correct |
| Bad checksum / invalid hex / wrong station / short positive | External model: errors9 /10 /8 /8; invalid position |
| Servo alarm / negative status | External model: errors12 /11; invalid position |
| Signed32 boundaries | External model: 7FFFFFFF=2147483647;80000000=-2147483648;FFFFFFFF=-1 |
| Coalesced/split RX packing | External model: chunks1,2,3,6,14 bytes preserve signed boundary |
| No-response retry bound | External10ms scan model: three attempts then error5, Busy false; no further resend |
| Native simulation | NOT RUN; timing/SDK transport behavior CHƯA XÁC MINH |
| Hardware response or position | NOT TESTED / NOT AUTHORIZED |

External checks executed in the current Eval session, not an unrelated C# application. They validate a reference transcription, not PLC runtime equivalence. Native compile validates GX syntax/types, not physical serial behavior.

## Bench Watch list and behavior

- Keep `BenchEnable=FALSE`, `ManualRead=FALSE`; no trigger/polling by default. Standby POU is not scheduled for automatic scan execution. Future execution/scheduling is a separately authorized commissioning action.
- Controls: `BenchEnable`, `ManualRead`, `PrevRead`, `StartEdge`, `ResetResult`, `StartupReady`.
- State/diagnostic: `Busy`, `State`, `PreviousState`, `Attempt`, `ErrorCode`, `LastError`, `Elapsed`, `RetryElapsed`, `TxControl[0..2]`, `RxControl[0..3]`, `TxDone[0..1]`, `RxDone[0..1]`.
- Result: `Raw[0..31]`, `Count`, `ServoAlarm`, `ReadSuccess`, `PositionValid`, signed DINT `Position` in command-pulse units. Position is the **last verified response**, not continuously fresh telemetry; validity clears on next accepted request, disable or failure. Do not use as homed/ready/safe-position feedback.
- Manual rising edge only after3.5s continuous BenchEnable; a too-early edge returns13 and must be released/retriggered. This delay does not measure actual servo power; after any servo power cycle, disarm/re-arm and obey the manual startup delay.
- TX/clear/retry-idle phase bound500ms; response/frame bound300ms after TX completion; EOT and100ms delay before retry; maximum3 total requests. Each timed phase resets TON on state change. Disable invalidates/disarms local work but **does not cancel a transmission already accepted by the C24**; remain disarmed until the transport is idle before a later authorized test.
- Error codes:1 disabled,2 config,3 drain/clear timeout,4 TX/idle timeout,5 RX timeout,6 RX result/count,7 overflow,8 framing/station/length,9 checksum,10 ASCII hex,11 negative status,12 servo alarm,13 startup guard. LastError preserves previous retry failure; ReadSuccess is latched until reset/next request/disable.
- Auto-allocation in final compile: D12164..D12287 (124 words), M8166..M8191 (26 bits), P2048..P2120 (73 pointers), T2045..T2047 (3 timers); these are compiler outputs, **not manually reserved devices to write**. Rebuild may reallocate them; use labels.

## Execution blocker and group evidence

Five actual jobs were spawned: G1-Lead, G1-Evidence, G1-Builder, G1-Challenger, G1-Verifier. They were reported running for approximately36minutes, but inspected histories contained only the assignment/IRC and no assistant tool actions or results. They were cancelled. Two recovery workers G1-BuilderRecovery / G1-ReviewRecovery likewise produced no tool evidence and were cancelled. Runtime resolved all to `9router/adurauma36/DOCS`; per-call model override was disabled. No worker completion, cross-examination, independent verification, or native change is claimed. Tool runtime anomaly was reported through xd://report_issue.

G3 targeted review jobs actually launched: `G3-Lead`, `G3-Evidence`, `G3-Builder`, `G3-Challenger`, `G3-Verifier`. Round2 findings were broadcast successfully to all five. After approximately23minutes no result was published; inspected Evidence history contained only its assignment. All five were cancelled with explicit cancelled results. No independent review or cross-examination outcome is claimed. Acceptance is limited to native compiler evidence and coordinator-run external models, not production commissioning.

## Remaining safe steps

1. Before hardware: separately authorize the exact test and verify wiring/termination, MR-J4-A firmware A3+, USB/RS422 exclusivity, PA03 communication ABS selection2, PC20 station0, PC21 baud/response delay, C24 CH2 nonprocedure19200 8E1/byte units/count1/no delimiter, and safe isolated machine conditions. PA03 full bitfield and actual live parameter values remain unverified; do not write a bare whole-parameter value based on this checklist.
2. No commissioning performed here. No SON/home/QD75 counter synchronization. Command02/data91 also sets an internal amplifier position-command value; do not call it side-effect-free. Actual serial timing, response and position require separate hardware validation.
3. Source/parser native simulation and independent reviewer results remain CHƯA XÁC MINH. Preserve backup and current saved source; never infer deployment readiness from the offline compiler.
