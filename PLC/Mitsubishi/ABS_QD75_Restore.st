(* ============================================================================================== *)
(* PROGRAM : ABS_QD75_Restore [PRG] Program [ST] - Robot_SCARA                                     *)
(* TARGET  : Q06UDEHCPU + QD75D4 (U0) + QJ71C24N CH2 (U8)                                          *)
(* SCOPE   : MR-J4-A command 02/data 91 ABS read for SCARA J1..J4, convert, then restore the       *)
(*           QD75D4 current feed value with start No. 9003 (no motion) and clear the OPR request   *)
(*           with Cd.19. No servo enable, no motion start, no safety output.                       *)
(* CONFIG  : Retentive D3000..D3095 (4 blocks x 24 words). Set PLC Parameter latch range to       *)
(*           D3000..D3095 before use; otherwise the startup checksum check fails with 903.         *)
(* MAIN    : Add ONE parallel branch per Y10..Y13 coil from M111..M114 (see                          *)
(*           ABS_QD75_Restore_MAIN.txt). MAIN keeps exclusive ownership of Y0/Y10..Y13/Y20..Y25.   *)
(* OFFLINE : Source artifact only. No download, RUN/STOP, force, parameter write or module write.  *)
(*                                                                                                *)
(* GX IMPORT (offline):                                                                            *)
(*   0) Device Test D3000..D3095 and M111..M131 free; if busy move the whole block/bit range.      *)
(*   1) Robot_SCARA > POU > Program > New: ABS_QD75_Restore, language ST.                          *)
(*   2) Local Label > Read from CSV File: ABS_QD75_Restore_labels.csv.                             *)
(*   3) Paste this body; Project > Rebuild All; require Error/Warning/CheckWarning = 0.            *)
(*   4) Read the editor back (Ctrl+A/Ctrl+C) and compare with this file, not just the compile OK.  *)
(*   5) Add ABS_QD75_Restore to Scan Program; keep ABS_Home out of scan/standby.                  *)
(*   6) PLC Parameter > Latch range: D3000..D3095.                                                 *)
(*   7) MAIN: add the 4 parallel branches from ABS_QD75_Restore_MAIN.txt, one coil per Y10..Y13.   *)
(*   Do not download, RUN/STOP, force, or write parameters while importing this source.            *)
(*                                                                                                *)
(* OFFLINE BENCH CHECK (no QD75/servo pulses): M129 load default config; edit GearNum/GearDen/     *)
(* LeadUm; M130 recompute ScaleNum/ScaleDen and checksum; M131 reset fault. Those steps never       *)
(* start a QD75 pulse and never write QD75 BFM.                                                    *)
(*                                                                                                *)
(* CONFIG BLOCK OFFSETS (word): +0 Mode (0 rotary wrap, 1 linear, 2 rotary no-wrap)                *)
(* +1 Station (0..9), +2..3 MotorPulsesPerRev, +4..5 GearNum, +6..7 GearDen, +8..9 LeadUm,         *)
(* +10..11 ZeroPulse, +12..13 HomeValue, +14..15 ScaleNum, +16..17 ScaleDen, +18..19 LastAbsPulse, *)
(* +20 Status (0 init, 1 OK, 2 fault), +21 Error, +22 Magic 16#A5C3, +23 Checksum (+0..+17 + Magic)*)
(*                                                                                                *)
(* ERRORS  : 1 disabled, 2 CH2 config/station, 4 TX, 5 timeout, 6 RX, 7 overflow, 8 frame,         *)
(*           9 checksum, 10 hex, 11 status, 12 servo alarm, 13 not ready,                          *)
(*           901 QD75 not ready, 902 ABS read, 903 config, 904 conversion/limit, 905 start, 906 verify *)
(* ============================================================================================== *)

(* ============================ 0. FIRST SCAN INIT ============================ *)
IF SM402 OR NOT bInit THEN
    SyncState := 0;
    SyncStep := 0;
    AxisIndex := 1;
    SetHomeAxis := 0;
    FaultAxis := 1;
    Confirm := 0;
    DirtyAxis := 1;
    bSampleValid := FALSE;
    bHomeReq := FALSE;
    bHomeMode := FALSE;
    bAutoStart := TRUE;
    bReadTimerRun := FALSE;
    bReadTimerRestart := FALSE;
    bSyncTimerRun := FALSE;
    bSyncTimerRestart := FALSE;
    bAckTimerRun := FALSE;
    bAckTimerRestart := FALSE;
    bCfgDirty := FALSE;
    bConvFault := FALSE;
    ReadReq := FALSE;
    PrevReadReq := FALSE;
    M111 := FALSE;
    M112 := FALSE;
    M113 := FALSE;
    M114 := FALSE;
    M120 := FALSE;
    M121 := FALSE;
    M122 := FALSE;
    FOR i := 1 TO 4 DO
        bAxisFresh[i] := FALSE;
        diQ[i] := 0;
        diAbsPulse[i] := 0;
    END_FOR;
    bInit := TRUE;
END_IF;

(* ============================ 1. OPERATOR PULSES ============================ *)
bLoadDefaults := M129 AND NOT PrevM129;
bApplyCfg := M130 AND NOT PrevM130;
bResetPulse := M131 AND NOT PrevM131;
PrevM129 := M129;
PrevM130 := M130;
PrevM131 := M131;

IF M125 AND NOT PrevM125 THEN
    SetHomeAxis := 1;
    bHomeReq := TRUE;
END_IF;
IF M126 AND NOT PrevM126 THEN
    SetHomeAxis := 2;
    bHomeReq := TRUE;
END_IF;
IF M127 AND NOT PrevM127 THEN
    SetHomeAxis := 3;
    bHomeReq := TRUE;
END_IF;
IF M128 AND NOT PrevM128 THEN
    SetHomeAxis := 4;
    bHomeReq := TRUE;
END_IF;
PrevM125 := M125;
PrevM126 := M126;
PrevM127 := M127;
PrevM128 := M128;

(* Reset only when no restore/set-home cycle is busy and CH2 is idle. *)
IF bResetPulse AND NOT M122 AND NOT Busy AND NOT X89 AND NOT X8A AND NOT X8B THEN
    AbsError := 0;
    SyncState := 0;
    SyncStep := 0;
    bAutoStart := TRUE;
    bHomeReq := FALSE;
    bHomeMode := FALSE;
    bSampleValid := FALSE;
    ReadReq := FALSE;
    M111 := FALSE;
    M112 := FALSE;
    M113 := FALSE;
    M114 := FALSE;
END_IF;

(* ============================ 2. M129 LOAD DEFAULTS ============================ *)
IF bLoadDefaults AND NOT M122 THEN
    FOR CfgIdx := 1 TO 4 DO
        CfgOfs := (CfgIdx - 1) * 24;
        awCfg[CfgOfs + 0] := 0;
        IF CfgIdx = 4 THEN
            awCfg[CfgOfs + 0] := 1;
        END_IF;
        awCfg[CfgOfs + 1] := CfgIdx - 1;
        awCfg[CfgOfs + 2] := 10000;
        awCfg[CfgOfs + 3] := 0;
        IF CfgIdx = 1 THEN
            awCfg[CfgOfs + 4] := 120;
        END_IF;
        IF CfgIdx = 2 THEN
            awCfg[CfgOfs + 4] := 100;
        END_IF;
        IF CfgIdx >= 3 THEN
            awCfg[CfgOfs + 4] := 2;
        END_IF;
        awCfg[CfgOfs + 5] := 0;
        awCfg[CfgOfs + 6] := 1;
        awCfg[CfgOfs + 7] := 0;
        awCfg[CfgOfs + 8] := 0;
        awCfg[CfgOfs + 9] := 0;
        IF CfgIdx = 4 THEN
            awCfg[CfgOfs + 8] := 15000;
        END_IF;
        awCfg[CfgOfs + 10] := 0;
        awCfg[CfgOfs + 11] := 0;
        awCfg[CfgOfs + 12] := 0;
        awCfg[CfgOfs + 13] := 0;
        IF CfgIdx = 1 THEN
            awCfg[CfgOfs + 14] := 30;
            awCfg[CfgOfs + 15] := 0;
            awCfg[CfgOfs + 16] := 1;
            awCfg[CfgOfs + 17] := 0;
        END_IF;
        IF CfgIdx = 2 THEN
            awCfg[CfgOfs + 14] := 36;
            awCfg[CfgOfs + 15] := 0;
            awCfg[CfgOfs + 16] := 1;
            awCfg[CfgOfs + 17] := 0;
        END_IF;
        IF CfgIdx = 3 THEN
            awCfg[CfgOfs + 14] := 1800;
            awCfg[CfgOfs + 15] := 0;
            awCfg[CfgOfs + 16] := 1;
            awCfg[CfgOfs + 17] := 0;
        END_IF;
        IF CfgIdx = 4 THEN
            awCfg[CfgOfs + 14] := 15;
            awCfg[CfgOfs + 15] := 0;
            awCfg[CfgOfs + 16] := 2;
            awCfg[CfgOfs + 17] := 0;
        END_IF;
        awCfg[CfgOfs + 18] := 0;
        awCfg[CfgOfs + 19] := 0;
        awCfg[CfgOfs + 20] := 0;
        awCfg[CfgOfs + 21] := 0;
        awCfg[CfgOfs + 22] := -23101;
        diSum := 0;
        FOR j := 0 TO 17 DO
            diSum := diSum + INT_TO_DINT(awCfg[CfgOfs + j]);
        END_FOR;
        diSum := diSum + 42435;
        diSum := diSum MOD 65536;
        IF diSum >= 32768 THEN
            diSum := diSum - 65536;
        END_IF;
        awCfg[CfgOfs + 23] := DINT_TO_INT(diSum);
    END_FOR;
    bCfgDirty := FALSE;
END_IF;

(* ============================ 3. M130 APPLY CONFIG ============================ *)
IF bApplyCfg AND NOT M122 THEN
    bConfigOk := TRUE;
    FOR CfgIdx := 1 TO 4 DO
        CfgOfs := (CfgIdx - 1) * 24;
        bAxisOk := TRUE;
        diLo := INT_TO_DINT(awCfg[CfgOfs + 2]);
        IF diLo < 0 THEN
            diLo := diLo + 65536;
        END_IF;
        diPulses := INT_TO_DINT(awCfg[CfgOfs + 3]) * 65536 + diLo;
        diLo := INT_TO_DINT(awCfg[CfgOfs + 4]);
        IF diLo < 0 THEN
            diLo := diLo + 65536;
        END_IF;
        diGearNum := INT_TO_DINT(awCfg[CfgOfs + 5]) * 65536 + diLo;
        diLo := INT_TO_DINT(awCfg[CfgOfs + 6]);
        IF diLo < 0 THEN
            diLo := diLo + 65536;
        END_IF;
        diGearDen := INT_TO_DINT(awCfg[CfgOfs + 7]) * 65536 + diLo;
        diLo := INT_TO_DINT(awCfg[CfgOfs + 8]);
        IF diLo < 0 THEN
            diLo := diLo + 65536;
        END_IF;
        diLead := INT_TO_DINT(awCfg[CfgOfs + 9]) * 65536 + diLo;
        IF (awCfg[CfgOfs + 0] < 0) OR (awCfg[CfgOfs + 0] > 2) OR
           (awCfg[CfgOfs + 1] < 0) OR (awCfg[CfgOfs + 1] > 9) OR
           (diPulses <= 0) OR (diGearNum <= 0) OR (diGearDen <= 0) OR (diLead < 0) THEN
            bAxisOk := FALSE;
        END_IF;
        IF bAxisOk THEN
            IF awCfg[CfgOfs + 0] = 1 THEN
                IF diLead > 2000000000 / diGearDen THEN
                    bAxisOk := FALSE;
                ELSE
                    diNum := diLead * diGearDen;
                    IF diGearNum > 2000000 THEN
                        bAxisOk := FALSE;
                    ELSE
                        diDen := diGearNum * 1000;
                    END_IF;
                END_IF;
            ELSE
                IF diGearDen > 59 THEN
                    bAxisOk := FALSE;
                ELSE
                    IF diPulses > 2000000000 / diGearNum THEN
                        bAxisOk := FALSE;
                    ELSE
                        diNum := 36000000 * diGearDen;
                        diDen := diPulses * diGearNum;
                    END_IF;
                END_IF;
            END_IF;
        END_IF;
        IF bAxisOk THEN
            IF (diNum <= 0) OR (diDen <= 0) OR (diDen > 2000000000) THEN
                bAxisOk := FALSE;
            END_IF;
        END_IF;
        IF bAxisOk THEN
            diTmp1 := diNum;
            diTmp2 := diDen;
            FOR j := 1 TO 32 DO
                IF diTmp2 <> 0 THEN
                    diSum := diTmp1 MOD diTmp2;
                    diTmp1 := diTmp2;
                    diTmp2 := diSum;
                END_IF;
            END_FOR;
            IF diTmp1 <= 0 THEN
                bAxisOk := FALSE;
            ELSE
                diSum := diNum / diTmp1;
                diTmp2 := diDen / diTmp1;
                diLo := diSum MOD 65536;
                IF diLo < 0 THEN
                    diLo := diLo + 65536;
                END_IF;
                awCfg[CfgOfs + 15] := DINT_TO_INT((diSum - diLo) / 65536);
                IF diLo > 32767 THEN
                    diLo := diLo - 65536;
                END_IF;
                awCfg[CfgOfs + 14] := DINT_TO_INT(diLo);
                diLo := diTmp2 MOD 65536;
                IF diLo < 0 THEN
                    diLo := diLo + 65536;
                END_IF;
                awCfg[CfgOfs + 17] := DINT_TO_INT((diTmp2 - diLo) / 65536);
                IF diLo > 32767 THEN
                    diLo := diLo - 65536;
                END_IF;
                awCfg[CfgOfs + 16] := DINT_TO_INT(diLo);
            END_IF;
        END_IF;
        IF bAxisOk THEN
            awCfg[CfgOfs + 20] := 0;
            awCfg[CfgOfs + 21] := 0;
        ELSE
            awCfg[CfgOfs + 20] := 2;
            awCfg[CfgOfs + 21] := 903;
            bConfigOk := FALSE;
        END_IF;
    END_FOR;
    FOR CfgIdx := 1 TO 4 DO
        CfgOfs := (CfgIdx - 1) * 24;
        awCfg[CfgOfs + 22] := -23101;
        diSum := 0;
        FOR j := 0 TO 17 DO
            diSum := diSum + INT_TO_DINT(awCfg[CfgOfs + j]);
        END_FOR;
        diSum := diSum + 42435;
        diSum := diSum MOD 65536;
        IF diSum >= 32768 THEN
            diSum := diSum - 65536;
        END_IF;
        awCfg[CfgOfs + 23] := DINT_TO_INT(diSum);
    END_FOR;
    bCfgDirty := FALSE;
    IF NOT bConfigOk THEN
        M121 := TRUE;
        M120 := FALSE;
        AbsError := 903;
        SyncState := 90;
        SyncStep := 0;
    END_IF;
END_IF;

(* ============================ 4. TIMERS AND ENABLES ============================ *)
bReadEnable := (SyncState = 20) OR (SyncState = 60);

IF bReadTimerRestart THEN
    bReadTimerRun := FALSE;
    bReadTimerRestart := FALSE;
ELSE
    bReadTimerRun := bReadEnable;
END_IF;
IF bSyncTimerRestart THEN
    bSyncTimerRun := FALSE;
    bSyncTimerRestart := FALSE;
ELSE
    bSyncTimerRun := (SyncState = 40);
END_IF;
IF bAckTimerRestart THEN
    bAckTimerRun := FALSE;
    bAckTimerRestart := FALSE;
ELSE
    bAckTimerRun := (SyncState = 40) AND (SyncStep = 5);
END_IF;

ReadyTimer(IN := TRUE, PT := T#3500ms, Q := StartupReady);
StartupTimer(IN := (SyncState = 10), PT := T#3500ms, Q := Startup3s);
StartupFaultTimer(IN := (SyncState = 10) AND Startup3s, PT := T#10000ms, Q := StartupFault);
ReadTimer(IN := bReadTimerRun, PT := T#15000ms, Q := ReadTimeout);
SyncTimer(IN := bSyncTimerRun, PT := T#2000ms, Q := SyncTimeoutQ);
AckTimer(IN := bAckTimerRun, PT := T#100ms, Q := AckTimerQ);

(* ============================ 5. SCHEDULER ============================ *)
CASE SyncState OF

0:
    M120 := FALSE;
    M121 := FALSE;
    M122 := FALSE;
    M111 := FALSE;
    M112 := FALSE;
    M113 := FALSE;
    M114 := FALSE;
    IF bAutoStart AND NOT M121 THEN
        bAutoStart := FALSE;
        SyncStep := 0;
        SyncState := 10;
    END_IF;

10:
    M122 := TRUE;
    M120 := FALSE;
    M121 := FALSE;
    IF Startup3s THEN
        bConfigOk := TRUE;
        FOR CfgIdx := 1 TO 4 DO
            CfgOfs := (CfgIdx - 1) * 24;
            IF awCfg[CfgOfs + 22] <> -23101 THEN
                bConfigOk := FALSE;
            END_IF;
            IF (awCfg[CfgOfs + 0] < 0) OR (awCfg[CfgOfs + 0] > 2) OR
               (awCfg[CfgOfs + 1] < 0) OR (awCfg[CfgOfs + 1] > 9) THEN
                bConfigOk := FALSE;
            END_IF;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 2]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diPulses := INT_TO_DINT(awCfg[CfgOfs + 3]) * 65536 + diLo;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 14]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diScaleN := INT_TO_DINT(awCfg[CfgOfs + 15]) * 65536 + diLo;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 16]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diScaleD := INT_TO_DINT(awCfg[CfgOfs + 17]) * 65536 + diLo;
            IF (diPulses <= 0) OR (diScaleN <= 0) OR (diScaleD <= 0) THEN
                bConfigOk := FALSE;
            END_IF;
            diSum := 0;
            FOR j := 0 TO 17 DO
                diSum := diSum + INT_TO_DINT(awCfg[CfgOfs + j]);
            END_FOR;
            diSum := diSum + 42435;
            diSum := diSum MOD 65536;
            IF diSum >= 32768 THEN
                diSum := diSum - 65536;
            END_IF;
            IF diSum <> INT_TO_DINT(awCfg[CfgOfs + 23]) THEN
                bConfigOk := FALSE;
            END_IF;
        END_FOR;
        IF NOT bConfigOk THEN
            AbsError := 903;
            SyncState := 90;
            SyncStep := 0;
        ELSIF Y0 AND X0 AND NOT X8 AND NOT X9 AND NOT XA AND NOT XB AND
              NOT XC AND NOT XD AND NOT XE AND NOT XF AND
              NOT X10 AND NOT X11 AND NOT X12 AND NOT X13 AND
              NOT Y20 AND NOT Y21 AND NOT Y22 AND NOT Y23 AND
              NOT M20 AND NOT M21 AND NOT M1000 AND NOT M105 THEN
            SyncState := 20;
            SyncStep := 0;
        END_IF;
    END_IF;
    IF StartupFault THEN
        AbsError := 901;
        SyncState := 90;
        SyncStep := 0;
    END_IF;

20:
    M122 := TRUE;
    M120 := FALSE;
    M121 := FALSE;

    IF SyncStep = 0 THEN
        AxisIndex := 1;
        Confirm := 0;
        bSampleValid := FALSE;
        SyncStep := 1;
    ELSIF SyncStep = 1 THEN
        CfgOfs := (AxisIndex - 1) * 24;
        AbsStation := awCfg[CfgOfs + 1];
        AbsValid := FALSE;
        AbsFresh := FALSE;
        AbsAlarm := FALSE;
        ReadReq := TRUE;
        bReadTimerRestart := TRUE;
        SyncStep := 2;
    ELSIF SyncStep = 2 THEN
        IF Busy THEN
            ReadReq := FALSE;
            SyncStep := 3;
        END_IF;
        IF ReadTimeout THEN
            AbsError := 902;
            SyncState := 90;
            SyncStep := 0;
            ReadReq := FALSE;
        END_IF;
    ELSIF SyncStep = 3 THEN
        IF NOT Busy THEN
            IF AbsValid AND NOT AbsAlarm THEN
                IF NOT bSampleValid THEN
                    PrevSample := AbsPosition;
                    bSampleValid := TRUE;
                    SyncStep := 1;
                ELSIF AbsPosition = PrevSample THEN
                    diAbsPulse[AxisIndex] := AbsPosition;
                    CfgOfs := (AxisIndex - 1) * 24;
                    diTmp1 := AbsPosition;
                    diLo := diTmp1 MOD 65536;
                    IF diLo < 0 THEN
                        diLo := diLo + 65536;
                    END_IF;
                    awCfg[CfgOfs + 19] := DINT_TO_INT((diTmp1 - diLo) / 65536);
                    IF diLo > 32767 THEN
                        diLo := diLo - 65536;
                    END_IF;
                    awCfg[CfgOfs + 18] := DINT_TO_INT(diLo);
                    awCfg[CfgOfs + 20] := 1;
                    awCfg[CfgOfs + 21] := 0;
                    bCfgDirty := TRUE;
                    DirtyAxis := AxisIndex;
                    bAxisFresh[AxisIndex] := TRUE;
                    bSampleValid := FALSE;
                    Confirm := 0;
                    IF AxisIndex < 4 THEN
                        AxisIndex := AxisIndex + 1;
                        SyncStep := 1;
                    ELSE
                        SyncState := 30;
                        SyncStep := 0;
                    END_IF;
                ELSE
                    Confirm := Confirm + 1;
                    IF Confirm >= 3 THEN
                        AbsError := 902;
                        CfgOfs := (AxisIndex - 1) * 24;
                        awCfg[CfgOfs + 20] := 2;
                        awCfg[CfgOfs + 21] := 902;
                        bCfgDirty := TRUE;
                        DirtyAxis := AxisIndex;
                        SyncState := 90;
                        SyncStep := 0;
                        ReadReq := FALSE;
                    ELSE
                        bSampleValid := FALSE;
                        SyncStep := 1;
                    END_IF;
                END_IF;
            ELSE
                AbsError := 902;
                CfgOfs := (AxisIndex - 1) * 24;
                awCfg[CfgOfs + 20] := 2;
                awCfg[CfgOfs + 21] := 902;
                bCfgDirty := TRUE;
                DirtyAxis := AxisIndex;
                SyncState := 90;
                SyncStep := 0;
                ReadReq := FALSE;
            END_IF;
        END_IF;
        IF ReadTimeout THEN
            AbsError := 902;
            SyncState := 90;
            SyncStep := 0;
            ReadReq := FALSE;
        END_IF;
    END_IF;

30:
    M122 := TRUE;
    M120 := FALSE;
    M121 := FALSE;
    bConvFault := FALSE;
    FOR CfgIdx := 1 TO 4 DO
        IF NOT bConvFault THEN
            CfgOfs := (CfgIdx - 1) * 24;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 10]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diZero := INT_TO_DINT(awCfg[CfgOfs + 11]) * 65536 + diLo;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 12]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diHome := INT_TO_DINT(awCfg[CfgOfs + 13]) * 65536 + diLo;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 14]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diScaleN := INT_TO_DINT(awCfg[CfgOfs + 15]) * 65536 + diLo;
            diLo := INT_TO_DINT(awCfg[CfgOfs + 16]);
            IF diLo < 0 THEN
                diLo := diLo + 65536;
            END_IF;
            diScaleD := INT_TO_DINT(awCfg[CfgOfs + 17]) * 65536 + diLo;
            IF (diScaleN <= 0) OR (diScaleD <= 0) THEN
                bConvFault := TRUE;
                AbsError := 903;
                FaultAxis := CfgIdx;
            ELSE
                diDelta := diAbsPulse[CfgIdx] - diZero;
                IF diDelta < 0 THEN
                    diMag := -diDelta;
                ELSE
                    diMag := diDelta;
                END_IF;
                IF diMag > 2000000000 / diScaleN THEN
                    bConvFault := TRUE;
                    AbsError := 904;
                    FaultAxis := CfgIdx;
                ELSE
                    diTmp2 := (diDelta * diScaleN) / diScaleD;
                    IF ((diTmp2 > 0) AND (diHome > (2000000000 - diTmp2))) OR
                       ((diTmp2 < 0) AND (diHome < (-2000000000 - diTmp2))) THEN
                        bConvFault := TRUE;
                        AbsError := 904;
                        FaultAxis := CfgIdx;
                    ELSE
                        diQcalc := diHome + diTmp2;
                        IF awCfg[CfgOfs + 0] = 0 THEN
                            diQcalc := diQcalc MOD 36000000;
                            IF diQcalc < 0 THEN
                                diQcalc := diQcalc + 36000000;
                            END_IF;
                        END_IF;
                        IF CfgIdx = 1 THEN
                            FROM(TRUE, H0, 18, 2, diPrUpper);
                            FROM(TRUE, H0, 20, 2, diPrLower);
                        ELSIF CfgIdx = 2 THEN
                            FROM(TRUE, H0, 168, 2, diPrUpper);
                            FROM(TRUE, H0, 170, 2, diPrLower);
                        ELSIF CfgIdx = 3 THEN
                            FROM(TRUE, H0, 318, 2, diPrUpper);
                            FROM(TRUE, H0, 320, 2, diPrLower);
                        ELSE
                            FROM(TRUE, H0, 468, 2, diPrUpper);
                            FROM(TRUE, H0, 470, 2, diPrLower);
                        END_IF;
                        IF (diPrUpper = 0) AND (diPrLower = 0) THEN
                            bConvFault := TRUE;
                            AbsError := 904;
                            FaultAxis := CfgIdx;
                        ELSIF (diQcalc < diPrLower) OR (diQcalc > diPrUpper) THEN
                            bConvFault := TRUE;
                            AbsError := 904;
                            FaultAxis := CfgIdx;
                        ELSE
                            diQ[CfgIdx] := diQcalc;
                        END_IF;
                    END_IF;
                END_IF;
            END_IF;
        END_IF;
    END_FOR;
    IF bConvFault THEN
        CfgOfs := (FaultAxis - 1) * 24;
        awCfg[CfgOfs + 20] := 2;
        awCfg[CfgOfs + 21] := AbsError;
        bCfgDirty := TRUE;
        DirtyAxis := FaultAxis;
        SyncState := 90;
        SyncStep := 0;
    ELSE
        SyncState := 40;
        SyncStep := 0;
        bSyncTimerRestart := TRUE;
    END_IF;

40:
    M122 := TRUE;
    M120 := FALSE;
    M121 := FALSE;

    IF SyncStep = 0 THEN
        IF NOT bHomeMode THEN
            AxisIndex := 1;
        END_IF;
        SyncStep := 1;
        bSyncTimerRestart := TRUE;
    ELSIF SyncStep = 1 THEN
        M111 := FALSE;
        M112 := FALSE;
        M113 := FALSE;
        M114 := FALSE;
        IF AxisIndex = 1 THEN
            FROM(TRUE, H0, 1500, 1, wPrevStart);
            DTO(TRUE, diQ[1], H0, 1506, 1);
            TO(TRUE, 9003, H0, 1500, 1);
        ELSIF AxisIndex = 2 THEN
            FROM(TRUE, H0, 1600, 1, wPrevStart);
            DTO(TRUE, diQ[2], H0, 1606, 1);
            TO(TRUE, 9003, H0, 1600, 1);
        ELSIF AxisIndex = 3 THEN
            FROM(TRUE, H0, 1700, 1, wPrevStart);
            DTO(TRUE, diQ[3], H0, 1706, 1);
            TO(TRUE, 9003, H0, 1700, 1);
        ELSE
            FROM(TRUE, H0, 1800, 1, wPrevStart);
            DTO(TRUE, diQ[4], H0, 1806, 1);
            TO(TRUE, 9003, H0, 1800, 1);
        END_IF;
        IF AxisIndex = 1 THEN
            M111 := TRUE;
        END_IF;
        IF AxisIndex = 2 THEN
            M112 := TRUE;
        END_IF;
        IF AxisIndex = 3 THEN
            M113 := TRUE;
        END_IF;
        IF AxisIndex = 4 THEN
            M114 := TRUE;
        END_IF;
        bSyncTimerRestart := TRUE;
        SyncStep := 2;
    ELSIF SyncStep = 2 THEN
        M111 := FALSE;
        M112 := FALSE;
        M113 := FALSE;
        M114 := FALSE;
        bAxisStartDone := FALSE;
        bAxisBusy := FALSE;
        IF AxisIndex = 1 THEN
            bAxisStartDone := X10;
            bAxisBusy := XC;
        END_IF;
        IF AxisIndex = 2 THEN
            bAxisStartDone := X11;
            bAxisBusy := XD;
        END_IF;
        IF AxisIndex = 3 THEN
            bAxisStartDone := X12;
            bAxisBusy := XE;
        END_IF;
        IF AxisIndex = 4 THEN
            bAxisStartDone := X13;
            bAxisBusy := XF;
        END_IF;
        IF bAxisStartDone AND NOT bAxisBusy THEN
            SyncStep := 3;
            bSyncTimerRestart := TRUE;
        END_IF;
        IF SyncTimeoutQ THEN
            AbsError := 905;
            CfgOfs := (AxisIndex - 1) * 24;
            awCfg[CfgOfs + 20] := 2;
            awCfg[CfgOfs + 21] := 905;
            bCfgDirty := TRUE;
            DirtyAxis := AxisIndex;
            SyncState := 90;
            SyncStep := 0;
        END_IF;
    ELSIF SyncStep = 3 THEN
        diRead := 0;
        IF AxisIndex = 1 THEN
            FROM(TRUE, H0, 800, 2, diRead);
        ELSIF AxisIndex = 2 THEN
            FROM(TRUE, H0, 900, 2, diRead);
        ELSIF AxisIndex = 3 THEN
            FROM(TRUE, H0, 1000, 2, diRead);
        ELSE
            FROM(TRUE, H0, 1100, 2, diRead);
        END_IF;
        IF diRead <> diQ[AxisIndex] THEN
            AbsError := 906;
            CfgOfs := (AxisIndex - 1) * 24;
            awCfg[CfgOfs + 20] := 2;
            awCfg[CfgOfs + 21] := 906;
            bCfgDirty := TRUE;
            DirtyAxis := AxisIndex;
            SyncState := 90;
            SyncStep := 0;
        ELSE
            SyncStep := 4;
        END_IF;
    ELSIF SyncStep = 4 THEN
        IF AxisIndex = 1 THEN
            TO(TRUE, 1, H0, 1521, 1);
        ELSIF AxisIndex = 2 THEN
            TO(TRUE, 1, H0, 1621, 1);
        ELSIF AxisIndex = 3 THEN
            TO(TRUE, 1, H0, 1721, 1);
        ELSE
            TO(TRUE, 1, H0, 1821, 1);
        END_IF;
        bAckTimerRestart := TRUE;
        SyncStep := 5;
    ELSIF SyncStep = 5 THEN
        IF AckTimerQ THEN
            IF AxisIndex = 1 THEN
                FROM(TRUE, H0, 817, 1, wStatus);
            ELSIF AxisIndex = 2 THEN
                FROM(TRUE, H0, 917, 1, wStatus);
            ELSIF AxisIndex = 3 THEN
                FROM(TRUE, H0, 1017, 1, wStatus);
            ELSE
                FROM(TRUE, H0, 1117, 1, wStatus);
            END_IF;
            IF (wStatus AND 8) <> 0 THEN
                AbsError := 906;
                CfgOfs := (AxisIndex - 1) * 24;
                awCfg[CfgOfs + 20] := 2;
                awCfg[CfgOfs + 21] := 906;
                bCfgDirty := TRUE;
                DirtyAxis := AxisIndex;
                SyncState := 90;
                SyncStep := 0;
            ELSE
                SyncStep := 6;
            END_IF;
        END_IF;
        IF SyncTimeoutQ THEN
            AbsError := 906;
            CfgOfs := (AxisIndex - 1) * 24;
            awCfg[CfgOfs + 20] := 2;
            awCfg[CfgOfs + 21] := 906;
            bCfgDirty := TRUE;
            DirtyAxis := AxisIndex;
            SyncState := 90;
            SyncStep := 0;
        END_IF;
    ELSIF SyncStep = 6 THEN
        IF AxisIndex = 1 THEN
            TO(TRUE, wPrevStart, H0, 1500, 1);
        ELSIF AxisIndex = 2 THEN
            TO(TRUE, wPrevStart, H0, 1600, 1);
        ELSIF AxisIndex = 3 THEN
            TO(TRUE, wPrevStart, H0, 1700, 1);
        ELSE
            TO(TRUE, wPrevStart, H0, 1800, 1);
        END_IF;
        IF bHomeMode THEN
            bHomeMode := FALSE;
            SyncState := 50;
            SyncStep := 0;
        ELSIF AxisIndex < 4 THEN
            AxisIndex := AxisIndex + 1;
            SyncStep := 1;
        ELSE
            SyncState := 50;
            SyncStep := 0;
        END_IF;
    END_IF;

50:
    M120 := TRUE;
    M121 := FALSE;
    M122 := FALSE;
    M111 := FALSE;
    M112 := FALSE;
    M113 := FALSE;
    M114 := FALSE;
    IF bHomeReq THEN
        IF (SetHomeAxis >= 1) AND (SetHomeAxis <= 4) AND
           bAxisFresh[SetHomeAxis] AND
           NOT Y20 AND NOT Y21 AND NOT Y22 AND NOT Y23 AND
           NOT M20 AND NOT M21 AND NOT M1000 AND NOT M105 THEN
            bHomeReq := FALSE;
            bHomeMode := FALSE;
            SyncState := 60;
            SyncStep := 0;
            Confirm := 0;
        END_IF;
    END_IF;

60:
    M122 := TRUE;
    M120 := FALSE;
    M121 := FALSE;
    IF SyncStep = 0 THEN
        AxisIndex := SetHomeAxis;
        IF (AxisIndex < 1) OR (AxisIndex > 4) THEN
            AbsError := 903;
            SyncState := 90;
            SyncStep := 0;
        ELSE
            CfgOfs := (AxisIndex - 1) * 24;
            AbsStation := awCfg[CfgOfs + 1];
            AbsValid := FALSE;
            AbsFresh := FALSE;
            AbsAlarm := FALSE;
            ReadReq := TRUE;
            bReadTimerRestart := TRUE;
            SyncStep := 1;
        END_IF;
    ELSIF SyncStep = 1 THEN
        IF Busy THEN
            ReadReq := FALSE;
            SyncStep := 2;
        END_IF;
        IF ReadTimeout THEN
            AbsError := 902;
            SyncState := 90;
            SyncStep := 0;
            ReadReq := FALSE;
        END_IF;
    ELSIF SyncStep = 2 THEN
        IF NOT Busy THEN
            IF AbsValid AND NOT AbsAlarm THEN
                CfgOfs := (AxisIndex - 1) * 24;
                diTmp1 := AbsPosition;
                diLo := diTmp1 MOD 65536;
                IF diLo < 0 THEN
                    diLo := diLo + 65536;
                END_IF;
                awCfg[CfgOfs + 11] := DINT_TO_INT((diTmp1 - diLo) / 65536);
                awCfg[CfgOfs + 19] := DINT_TO_INT((diTmp1 - diLo) / 65536);
                IF diLo > 32767 THEN
                    diLo := diLo - 65536;
                END_IF;
                awCfg[CfgOfs + 10] := DINT_TO_INT(diLo);
                awCfg[CfgOfs + 18] := DINT_TO_INT(diLo);
                awCfg[CfgOfs + 20] := 1;
                awCfg[CfgOfs + 21] := 0;
                bCfgDirty := TRUE;
                DirtyAxis := AxisIndex;
                diLo := INT_TO_DINT(awCfg[CfgOfs + 12]);
                IF diLo < 0 THEN
                    diLo := diLo + 65536;
                END_IF;
                diQ[AxisIndex] := INT_TO_DINT(awCfg[CfgOfs + 13]) * 65536 + diLo;
                bAxisFresh[AxisIndex] := TRUE;
                bHomeMode := TRUE;
                SyncState := 40;
                SyncStep := 0;
                bSyncTimerRestart := TRUE;
            ELSE
                AbsError := 902;
                CfgOfs := (AxisIndex - 1) * 24;
                awCfg[CfgOfs + 20] := 2;
                awCfg[CfgOfs + 21] := 902;
                bCfgDirty := TRUE;
                DirtyAxis := AxisIndex;
                SyncState := 90;
                SyncStep := 0;
            END_IF;
        END_IF;
        IF ReadTimeout THEN
            AbsError := 902;
            SyncState := 90;
            SyncStep := 0;
            ReadReq := FALSE;
        END_IF;
    END_IF;

90:
    M121 := TRUE;
    M120 := FALSE;
    M122 := FALSE;
    M111 := FALSE;
    M112 := FALSE;
    M113 := FALSE;
    M114 := FALSE;

END_CASE;

(* ============================ 6. CONFIG CHECKSUM REFRESH ============================ *)
IF bCfgDirty THEN
    IF (DirtyAxis >= 1) AND (DirtyAxis <= 4) THEN
        CfgOfs := (DirtyAxis - 1) * 24;
        diSum := 0;
        FOR j := 0 TO 17 DO
            diSum := diSum + INT_TO_DINT(awCfg[CfgOfs + j]);
        END_FOR;
        diSum := diSum + 42435;
        diSum := diSum MOD 65536;
        IF diSum >= 32768 THEN
            diSum := diSum - 65536;
        END_IF;
        awCfg[CfgOfs + 23] := DINT_TO_INT(diSum);
    END_IF;
    bCfgDirty := FALSE;
END_IF;

(* ============================ 7. ABS READ FSM (CH2) ============================ *)
StartEdge := ReadReq AND NOT PrevReadReq;
PrevReadReq := ReadReq;
TxFire := FALSE;
RxFire := FALSE;

WaitPreset := T#500ms;
IF State = 30 THEN
    WaitPreset := T#300ms;
END_IF;
WaitTimer(IN := (State = PreviousState) AND ((State = 10) OR (State = 11) OR
    (State = 20) OR (State = 30) OR (State = 40) OR (State = 50)),
    PT := WaitPreset, Q := TimerExpired, ET := Elapsed);
PreviousState := State;
RetryTimer(IN := (State = 12) OR (State = 60), PT := T#100ms,
    Q := RetryExpired, ET := RetryElapsed);

IF NOT bReadEnable THEN
    PositionValid := FALSE;
    Busy := FALSE;
    ReadSuccess := FALSE;
    AbsValid := FALSE;
    AbsFresh := FALSE;
    ErrorCode := 1;
    State := 0;
ELSE
    IF StartEdge AND NOT Busy AND NOT StartupReady THEN
        ErrorCode := 13;
    END_IF;
    IF StartEdge AND NOT Busy AND StartupReady THEN
        PositionValid := FALSE;
        ReadSuccess := FALSE;
        ServoAlarm := FALSE;
        AbsAlarm := FALSE;
        AbsValid := FALSE;
        AbsFresh := FALSE;
        ErrorCode := 0;
        LastError := 0;
        Attempt := 1;
        Count := 0;
        Busy := TRUE;
        State := 10;
    END_IF;
    CASE State OF
    10:
        IF X8A OR X8B THEN
            RxFire := NOT RxDone[0];
        ELSIF NOT X89 AND NOT TxDone[0] AND NOT RxDone[0] THEN
            IF (U8\G610 <> 6) OR ((U8\G611 AND HFF3F) <> H070E) OR
               (U8\G310 <> 1) OR (U8\G324 <> 1) OR (U8\G325 <> HFFFF) OR
               (U8\G449 <> 0) OR (U8\G447 <> 0) OR (U8\G448 <> 0) OR
               ((U8\G307 AND H0300) <> 0) OR (U8\G333 <> 0) THEN
                ErrorCode := 2;
                State := 90;
            ELSE
                U8\G328 := 1;
                State := 11;
            END_IF;
        END_IF;
        IF TimerExpired THEN
            ErrorCode := 3;
            State := 90;
        END_IF;
    11:
        IF U8\G328 = 0 THEN
            State := 12;
        END_IF;
        IF TimerExpired THEN
            ErrorCode := 3;
            State := 90;
        END_IF;
    12:
        IF RetryExpired THEN
            IF (AbsStation < 0) OR (AbsStation > 9) THEN
                ErrorCode := 2;
                State := 90;
            ELSE
                Count := 0;
                FOR ByteIndex := 0 TO 31 DO
                    Raw[ByteIndex] := 0;
                END_FOR;
                TxData[0] := INT_TO_WORD(12289 + AbsStation * 256);
                TxData[1] := H3230;
                TxData[2] := H3902;
                TxData[3] := H0331;
                ChecksumSum := 0;
                ChecksumSum := ChecksumSum + WORD_TO_INT(SHR(TxData[0], 8) AND H00FF);
                ChecksumSum := ChecksumSum + WORD_TO_INT(TxData[1] AND H00FF);
                ChecksumSum := ChecksumSum + WORD_TO_INT(SHR(TxData[1], 8) AND H00FF);
                ChecksumSum := ChecksumSum + WORD_TO_INT(TxData[2] AND H00FF);
                ChecksumSum := ChecksumSum + WORD_TO_INT(SHR(TxData[2], 8) AND H00FF);
                ChecksumSum := ChecksumSum + WORD_TO_INT(TxData[3] AND H00FF);
                ChecksumSum := ChecksumSum + WORD_TO_INT(SHR(TxData[3], 8) AND H00FF);
                Nibble := ChecksumSum MOD 256;
                Ch := Nibble / 16;
                IF Ch < 10 THEN
                    Ch := 48 + Ch;
                ELSE
                    Ch := 55 + Ch;
                END_IF;
                TxData[4] := INT_TO_WORD(Ch);
                Ch := Nibble MOD 16;
                IF Ch < 10 THEN
                    Ch := 48 + Ch;
                ELSE
                    Ch := 55 + Ch;
                END_IF;
                TxData[4] := TxData[4] OR INT_TO_WORD(Ch * 256);
                TxControl[2] := 10;
                TxFire := TRUE;
                State := 20;
            END_IF;
        END_IF;
    20:
        IF TxDone[0] THEN
            IF TxDone[1] THEN
                ErrorCode := 4;
                State := 90;
            ELSE
                State := 30;
            END_IF;
        ELSIF TimerExpired THEN
            ErrorCode := 4;
            State := 90;
        END_IF;
    30:
        IF X8A OR X8B THEN
            RxFire := NOT RxDone[0];
        END_IF;
        IF RxDone[0] THEN
            IF RxDone[1] OR (RxControl[2] < 1) OR (RxControl[2] > 32) THEN
                ErrorCode := 6;
                State := 40;
            ELSIF Count + RxControl[2] > 32 THEN
                ErrorCode := 7;
                State := 40;
            ELSE
                FOR ByteIndex := 0 TO RxControl[2] - 1 DO
                    IF (ByteIndex MOD 2) = 0 THEN
                        Raw[Count] := WORD_TO_INT(RxData[ByteIndex / 2] AND H00FF);
                    ELSE
                        Raw[Count] := WORD_TO_INT(SHR(RxData[ByteIndex / 2], 8) AND H00FF);
                    END_IF;
                    Count := Count + 1;
                END_FOR;
                IF Count >= 6 THEN
                    IF Raw[Count - 3] = 3 THEN
                        State := 35;
                    END_IF;
                END_IF;
            END_IF;
        END_IF;
        IF TimerExpired AND (State = 30) THEN
            ErrorCode := 5;
            State := 40;
        END_IF;
    35:
        ErrorCode := 0;
        IF (Raw[0] <> 2) OR (Raw[1] <> (48 + AbsStation)) OR (Raw[Count - 3] <> 3) THEN
            ErrorCode := 8;
        ELSE
            ChecksumSum := 0;
            FOR ByteIndex := 1 TO Count - 3 DO
                ChecksumSum := ChecksumSum + Raw[ByteIndex];
            END_FOR;
            ChecksumSum := ChecksumSum MOD 256;
            Expected := 0;
            FOR ByteIndex := Count - 2 TO Count - 1 DO
                Ch := Raw[ByteIndex];
                Nibble := -1;
                IF (Ch >= 48) AND (Ch <= 57) THEN
                    Nibble := Ch - 48;
                END_IF;
                IF (Ch >= 65) AND (Ch <= 70) THEN
                    Nibble := Ch - 55;
                END_IF;
                IF Nibble < 0 THEN
                    ErrorCode := 10;
                ELSE
                    Expected := Expected * 16 + Nibble;
                END_IF;
            END_FOR;
            IF (ErrorCode = 0) AND (Expected <> ChecksumSum) THEN
                ErrorCode := 9;
            END_IF;
            IF ErrorCode = 0 THEN
                IF Raw[2] = 97 THEN
                    ServoAlarm := TRUE;
                    AbsAlarm := TRUE;
                    ErrorCode := 12;
                ELSIF Raw[2] <> 65 THEN
                    ErrorCode := 11;
                ELSIF Count <> 14 THEN
                    ErrorCode := 8;
                ELSE
                    Accum := 0;
                    FOR ByteIndex := 3 TO 10 DO
                        Ch := Raw[ByteIndex];
                        Nibble := -1;
                        IF (Ch >= 48) AND (Ch <= 57) THEN
                            Nibble := Ch - 48;
                        END_IF;
                        IF (Ch >= 65) AND (Ch <= 70) THEN
                            Nibble := Ch - 55;
                        END_IF;
                        IF Nibble < 0 THEN
                            ErrorCode := 10;
                        ELSE
                            IF ByteIndex = 3 THEN
                                IF Nibble >= 8 THEN
                                    Accum := INT_TO_DINT(Nibble - 16);
                                ELSE
                                    Accum := INT_TO_DINT(Nibble);
                                END_IF;
                            ELSE
                                Accum := Accum * 16 + INT_TO_DINT(Nibble);
                            END_IF;
                        END_IF;
                    END_FOR;
                END_IF;
            END_IF;
        END_IF;
        IF ErrorCode = 0 THEN
            Position := Accum;
            PositionValid := TRUE;
            ReadSuccess := TRUE;
            Busy := FALSE;
            State := 0;
            AbsPosition := Accum;
            AbsValid := TRUE;
            AbsFresh := TRUE;
            AbsError := 0;
            AbsAttempt := Attempt;
        ELSIF ErrorCode = 12 THEN
            State := 90;
        ELSE
            State := 40;
        END_IF;
    40:
        LastError := ErrorCode;
        IF Attempt >= 3 THEN
            State := 90;
        ELSIF TimerExpired THEN
            ErrorCode := 4;
            State := 90;
        ELSIF NOT X89 AND NOT TxDone[0] AND NOT RxDone[0] THEN
            TxData[0] := H0004;
            TxControl[2] := 1;
            TxFire := TRUE;
            State := 50;
        END_IF;
    50:
        IF TxDone[0] THEN
            IF TxDone[1] THEN
                ErrorCode := 4;
                State := 90;
            ELSE
                State := 60;
            END_IF;
        ELSIF TimerExpired THEN
            ErrorCode := 4;
            State := 90;
        END_IF;
    60:
        IF RetryExpired THEN
            Attempt := Attempt + 1;
            State := 10;
        END_IF;
    90:
        LastError := ErrorCode;
        PositionValid := FALSE;
        Busy := FALSE;
        State := 0;
        AbsValid := FALSE;
        AbsFresh := FALSE;
        AbsError := ErrorCode;
        AbsAttempt := Attempt;
    END_CASE;
END_IF;

TxControl[0] := 2;
RxControl[0] := 2;
RxControl[3] := 16;
G_OUTPUT(TxFire, H8, TxControl, TxData[0], TxDone);
G_INPUT(RxFire, H8, RxControl, RxData[0], RxDone);
