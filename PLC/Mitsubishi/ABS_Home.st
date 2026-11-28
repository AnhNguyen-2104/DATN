(* ABS_Home (compact 2026-11-27): power-up MR-J4 ABS read + QD75 Md.20 preset only. *)
(* No motion and no PLC origin setting. CH2 (U8) is owned only by this POU; *)
(* first scan writes U8\G324:=1, U8\G325:=HFFFF. QD75 access is always computed FROM/TO with wBfm. *)
(* Flow: 0 init -> 10 startup wait -> 20 two equal 02 reads/axis -> 30 convert + Pr.12/13 -> *)
(*       40 Da.6 + Cd.3:=9003 + M11x + Md.20 verify + restore Cd.3 -> 50 ready; 90 fault. *)
(* Errors: 901 QD75, 902 ABS comm, 903 config, 904 limit, 905 start, 906 verify, 908 frame. *)
(* Frame: SOH|station|02|STX|91|ETX|checksum; checksum = low byte of sum station..ETX. *)
(* Home deadband: |raw - ZeroPulse| <= 10 pulses maps to 0 (encoder dither at calibrated datum). *)
(* Offline source only: no download, RUN/STOP, force, parameter or module write. *)

(* --- 0. FIRST SCAN: config + defaults --- *)
IF SM402 OR NOT bInit THEN
    SyncState := 0; SyncStep := 0; AxisIndex := 1; AxisMax := 2;
    AbsError := 0; FaultAxis := 1; Confirm := 0;
    bAutoStart := TRUE; bConfigOk := TRUE; bConvFault := FALSE; bSampleValid := FALSE;
    bReadTimerRun := FALSE; bReadTimerRestart := FALSE;
    bSyncTimerRun := FALSE; bSyncTimerRestart := FALSE;
    bAckTimerRun := FALSE; bAckTimerRestart := FALSE;
    ReadReq := FALSE; PrevReadReq := FALSE; PrevM131 := FALSE; bResetPulse := FALSE;
    bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
    State := 0; Attempt := 0; Count := 0; ErrorCode := 0; LastError := 0; PreviousState := 0;
    Busy := FALSE; ReadSuccess := FALSE; PositionValid := FALSE; ServoAlarm := FALSE;
    AbsValid := FALSE; AbsFresh := FALSE; AbsAlarm := FALSE;
    Position1 := 0; Position2 := 0; Position3 := 0; Position4 := 0;
    FOR iAxis := 1 TO 4 DO
        bAxisFresh[iAxis] := FALSE; diQ[iAxis] := 0; diAbsPulse[iAxis] := 0;
    END_FOR;
    FOR iAxis := 0 TO 95 DO awCfg[iAxis] := 0; END_FOR;
    (* J1: rotary, station 0, ScaleNum 30, ScaleDen 1, ZeroPulse 0, HomeValue 0 *)
    awCfg[0] := 2; awCfg[1] := 0; awCfg[14] := 30; awCfg[16] := 1;
    (* J2: rotary, station 1, ScaleNum 36, ScaleDen 1, ZeroPulse -1 (low/high HFFFF), HomeValue 0 *)
    awCfg[24] := 2; awCfg[25] := 1; awCfg[34] := -1; awCfg[35] := -1; awCfg[38] := 36; awCfg[40] := 1;
    U8\G324 := 1; U8\G325 := HFFFF; bInit := TRUE;
END_IF;

(* --- 1. M131 FAULT RESET --- *)
bResetPulse := M131 AND NOT PrevM131;
PrevM131 := M131;
IF bResetPulse AND NOT M122 AND NOT Busy THEN
    AbsError := 0; SyncState := 0; SyncStep := 0; AxisIndex := 1; Confirm := 0;
    bSampleValid := FALSE; bConvFault := FALSE; bAutoStart := TRUE;
    ReadReq := FALSE; PrevReadReq := FALSE;
    bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
    FOR iAxis := 1 TO 4 DO bAxisFresh[iAxis] := FALSE; END_FOR;
    Busy := FALSE; State := 0; Attempt := 0; ErrorCode := 0;
    AbsValid := FALSE; AbsFresh := FALSE; AbsAlarm := FALSE; PositionValid := FALSE;
END_IF;

(* --- 2. TIMERS --- *)
bReadEnable := (SyncState = 20);
IF bReadTimerRestart THEN
    bReadTimerRun := FALSE; bReadTimerRestart := FALSE;
ELSE
    bReadTimerRun := bReadEnable;
END_IF;
IF bSyncTimerRestart THEN
    bSyncTimerRun := FALSE; bSyncTimerRestart := FALSE;
ELSE
    bSyncTimerRun := (SyncState = 40);
END_IF;
IF bAckTimerRestart THEN
    bAckTimerRun := FALSE; bAckTimerRestart := FALSE;
ELSE
    bAckTimerRun := (SyncState = 40) AND (SyncStep = 3);
END_IF;
ReadyTimer(IN := TRUE, PT := T#3500ms, Q := StartupReady);
StartupTimer(IN := (SyncState = 10), PT := T#3500ms, Q := Startup3s);
StartupFaultTimer(IN := (SyncState = 10) AND Startup3s, PT := T#10000ms, Q := StartupFault);
ReadTimer(IN := bReadTimerRun, PT := T#15000ms, Q := ReadTimeout);
SyncTimer(IN := bSyncTimerRun, PT := T#2000ms, Q := SyncTimeoutQ);
AckTimer(IN := bAckTimerRun, PT := T#100ms, Q := AckTimerQ);
IF (M20 OR M21) AND ((SyncState = 20) OR (SyncState = 30)) THEN
    AbsError := 901; SyncState := 90; SyncStep := 0; ReadReq := FALSE;
END_IF;

(* --- 3. SCHEDULER --- *)
M122 := (SyncState = 20) OR (SyncState = 30) OR (SyncState = 40);
M120 := (SyncState = 50);
M121 := (SyncState = 90);
CASE SyncState OF
0:
    bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
    IF bAutoStart AND NOT M121 THEN
        bAutoStart := FALSE; SyncStep := 0; SyncState := 10;
    END_IF;
10:
    IF Startup3s THEN
        bConfigOk := TRUE;
        FOR CfgIdx := 1 TO AxisMax DO
            CfgOfs := (CfgIdx - 1) * 24;
            diScaleN := INT_TO_DINT(awCfg[CfgOfs + 15]) * 65536 + INT_TO_DINT(awCfg[CfgOfs + 14]);
            IF awCfg[CfgOfs + 14] < 0 THEN diScaleN := diScaleN + 65536; END_IF;
            diScaleD := INT_TO_DINT(awCfg[CfgOfs + 17]) * 65536 + INT_TO_DINT(awCfg[CfgOfs + 16]);
            IF awCfg[CfgOfs + 16] < 0 THEN diScaleD := diScaleD + 65536; END_IF;
            IF (awCfg[CfgOfs + 0] < 0) OR (awCfg[CfgOfs + 0] > 2) OR
            (awCfg[CfgOfs + 1] < 0) OR (awCfg[CfgOfs + 1] > 9) OR
            (diScaleN <= 0) OR (diScaleD <= 0) THEN
                bConfigOk := FALSE;
            END_IF;
        END_FOR;
        IF NOT bConfigOk THEN
            AbsError := 903; SyncState := 90; SyncStep := 0;
        ELSIF Y0 AND X0 AND
        NOT (X8 OR (AxisMax >= 2 AND X9) OR (AxisMax >= 3 AND XA) OR (AxisMax >= 4 AND XB)) AND
        NOT (XC OR (AxisMax >= 2 AND XD) OR (AxisMax >= 3 AND XE) OR (AxisMax >= 4 AND XF)) AND
        NOT M20 AND NOT M21 AND NOT M1000 AND NOT M105 AND NOT M122 THEN
            AxisIndex := 1; Confirm := 0; bSampleValid := FALSE;
            SyncState := 20; SyncStep := 0;
        END_IF;
    END_IF;
    IF StartupFault THEN AbsError := 901; SyncState := 90; SyncStep := 0; END_IF;
20:
    IF SyncStep = 0 THEN
        IF M20 OR M21 THEN AbsError := 901; SyncState := 90; SyncStep := 0;
        ELSE AxisIndex := 1; Confirm := 0; bSampleValid := FALSE; SyncStep := 1; END_IF;
    ELSIF SyncStep = 1 THEN
        CfgOfs := (AxisIndex - 1) * 24; AbsStation := awCfg[CfgOfs + 1];
        bAxisFresh[AxisIndex] := FALSE;
        AbsValid := FALSE; AbsFresh := FALSE; AbsAlarm := FALSE;
        ReadReq := TRUE; bReadTimerRestart := TRUE; SyncStep := 2;
    ELSIF SyncStep = 2 THEN
        IF Busy THEN ReadReq := FALSE; SyncStep := 3; END_IF;
        IF ReadTimeout THEN ReadReq := FALSE; AbsError := 902; SyncState := 90; SyncStep := 0; END_IF;
    ELSIF SyncStep = 3 THEN
        IF ReadTimeout THEN ReadReq := FALSE; AbsError := 902; SyncState := 90; SyncStep := 0;
        ELSIF NOT Busy THEN
            IF AbsValid AND NOT AbsAlarm THEN
                IF NOT bSampleValid THEN PrevSample := AbsPosition; bSampleValid := TRUE; SyncStep := 1;
                ELSIF AbsPosition = PrevSample THEN
                    diAbsPulse[AxisIndex] := AbsPosition; bAxisFresh[AxisIndex] := TRUE;
                    IF AxisIndex = 1 THEN Position1 := AbsPosition;
                    ELSIF AxisIndex = 2 THEN Position2 := AbsPosition;
                    ELSIF AxisIndex = 3 THEN Position3 := AbsPosition;
                    ELSE Position4 := AbsPosition; END_IF;
                    bSampleValid := FALSE; Confirm := 0;
                    IF AxisIndex < AxisMax THEN AxisIndex := AxisIndex + 1; SyncStep := 1;
                    ELSE SyncState := 30; SyncStep := 0; END_IF;
                ELSE
                    Confirm := Confirm + 1; bSampleValid := FALSE;
                    IF Confirm >= 3 THEN AbsError := 902; SyncState := 90; SyncStep := 0;
                    ELSE SyncStep := 1; END_IF;
                END_IF;
            ELSE
                IF (ErrorCode >= 8) AND (ErrorCode <= 11) THEN AbsError := 908; ELSE AbsError := 902; END_IF;
                SyncState := 90; SyncStep := 0; ReadReq := FALSE;
            END_IF;
        END_IF;
    END_IF;
30:
    bConvFault := FALSE;
    FOR CfgIdx := 1 TO AxisMax DO
        IF NOT bConvFault THEN
            CfgOfs := (CfgIdx - 1) * 24;
            diZero := INT_TO_DINT(awCfg[CfgOfs + 11]) * 65536 + INT_TO_DINT(awCfg[CfgOfs + 10]);
            IF awCfg[CfgOfs + 10] < 0 THEN diZero := diZero + 65536; END_IF;
            diHome := INT_TO_DINT(awCfg[CfgOfs + 13]) * 65536 + INT_TO_DINT(awCfg[CfgOfs + 12]);
            IF awCfg[CfgOfs + 12] < 0 THEN diHome := diHome + 65536; END_IF;
            diScaleN := INT_TO_DINT(awCfg[CfgOfs + 15]) * 65536 + INT_TO_DINT(awCfg[CfgOfs + 14]);
            IF awCfg[CfgOfs + 14] < 0 THEN diScaleN := diScaleN + 65536; END_IF;
            diScaleD := INT_TO_DINT(awCfg[CfgOfs + 17]) * 65536 + INT_TO_DINT(awCfg[CfgOfs + 16]);
            IF awCfg[CfgOfs + 16] < 0 THEN diScaleD := diScaleD + 65536; END_IF;
            IF (diScaleN <= 0) OR (diScaleD <= 0) THEN
                bConvFault := TRUE; AbsError := 903; FaultAxis := CfgIdx;
            ELSE
                diDelta := diAbsPulse[CfgIdx] - diZero;
                IF (diDelta >= -10) AND (diDelta <= 10) THEN
                    diDelta := 0;
                END_IF;
                IF diDelta < 0 THEN diMag := -diDelta; ELSE diMag := diDelta; END_IF;
                IF diMag > 2000000000 / diScaleN THEN
                    bConvFault := TRUE; AbsError := 904; FaultAxis := CfgIdx;
                ELSE
                    diTmp2 := (diDelta * diScaleN) / diScaleD;
                    IF ((diTmp2 > 0) AND (diHome > (2000000000 - diTmp2))) OR ((diTmp2 < 0) AND (diHome < (-2000000000 - diTmp2))) THEN
                        bConvFault := TRUE; AbsError := 904; FaultAxis := CfgIdx;
                    ELSE
                        diQcalc := diHome + diTmp2;
                        IF awCfg[CfgOfs + 0] = 0 THEN
                            diQcalc := diQcalc MOD 36000000;
                            IF diQcalc < 0 THEN diQcalc := diQcalc + 36000000; END_IF;
                        END_IF;
                        wBfm := 18 + 150*(CfgIdx-1);
                        FROM(TRUE, H0, wBfm, 1, wLoInt);
                        FROM(TRUE, H0, wBfm + 1, 1, wHiInt);
                        diPrUpper := INT_TO_DINT(wHiInt) * 65536 + INT_TO_DINT(wLoInt);
                        IF wLoInt < 0 THEN diPrUpper := diPrUpper + 65536; END_IF;
                        FROM(TRUE, H0, wBfm + 2, 1, wLoInt);
                        FROM(TRUE, H0, wBfm + 3, 1, wHiInt);
                        diPrLower := INT_TO_DINT(wHiInt) * 65536 + INT_TO_DINT(wLoInt);
                        IF wLoInt < 0 THEN diPrLower := diPrLower + 65536; END_IF;
                        IF (diPrUpper = 0) AND (diPrLower = 0) THEN
                            bConvFault := TRUE; AbsError := 904; FaultAxis := CfgIdx;
                        ELSIF (diQcalc < diPrLower) OR (diQcalc > diPrUpper) THEN
                            bConvFault := TRUE; AbsError := 904; FaultAxis := CfgIdx;
                        ELSE
                            diQ[CfgIdx] := diQcalc;
                        END_IF;
                    END_IF;
                END_IF;
            END_IF;
        END_IF;
    END_FOR;
    IF bConvFault THEN SyncState := 90; SyncStep := 0;
    ELSE SyncState := 40; SyncStep := 0; bSyncTimerRestart := TRUE; END_IF;
40:
    IF SyncStep = 0 THEN
        IF M20 OR M21 THEN AbsError := 901; SyncState := 90; SyncStep := 0;
        ELSE AxisIndex := 1; bSyncTimerRestart := TRUE; SyncStep := 1; END_IF;
    ELSIF SyncStep = 1 THEN
        bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
        IF M20 OR M21 OR X8 OR (AxisMax >= 2 AND X9) OR (AxisMax >= 3 AND XA) OR (AxisMax >= 4 AND XB) OR
        XC OR (AxisMax >= 2 AND XD) OR (AxisMax >= 3 AND XE) OR (AxisMax >= 4 AND XF) THEN
            AbsError := 901; SyncState := 90; SyncStep := 0;
        ELSE
            wBfm := 1500 + 100*(AxisIndex-1);
            FROM(TRUE, H0, wBfm, 1, wPrevStart);
            diHiS := diQ[AxisIndex] / 65536;
            IF (diQ[AxisIndex] - diHiS * 65536) < 0 THEN diHiS := diHiS - 1; END_IF;
            wHiInt := DINT_TO_INT(diHiS);
            wLoInt := DINT_TO_INT(diQ[AxisIndex] - diHiS * 65536);
            TO(TRUE, wLoInt, H0, wBfm + 6, 1);
            TO(TRUE, wHiInt, H0, wBfm + 7, 1);
            TO(TRUE, 9003, H0, wBfm, 1);
            bSyncTimerRestart := TRUE; SyncStep := 6;
        END_IF;
    ELSIF SyncStep = 6 THEN
        wBfm := 1500 + 100*(AxisIndex-1);
        FROM(TRUE, H0, wBfm, 1, wLoInt);
        IF M20 OR M21 OR X8 OR (AxisMax >= 2 AND X9) OR (AxisMax >= 3 AND XA) OR (AxisMax >= 4 AND XB) OR
        XC OR (AxisMax >= 2 AND XD) OR (AxisMax >= 3 AND XE) OR (AxisMax >= 4 AND XF) THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            TO(TRUE, wPrevStart, H0, wBfm, 1);
            AbsError := 901; SyncState := 90; SyncStep := 0;
        ELSIF wLoInt <> 9003 THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            TO(TRUE, wPrevStart, H0, wBfm, 1);
            AbsError := 906; SyncState := 90; SyncStep := 0;
        ELSE
            bM111 := (AxisIndex = 1); bM112 := (AxisIndex = 2);
            bM113 := (AxisIndex = 3); bM114 := (AxisIndex = 4);
            bSyncTimerRestart := TRUE; SyncStep := 2;
        END_IF;
    ELSIF SyncStep = 2 THEN
        bAxisStartDone := (AxisIndex = 1 AND X10) OR (AxisIndex = 2 AND X11) OR (AxisIndex = 3 AND X12) OR (AxisIndex = 4 AND X13);
        bAxisBusy := (AxisIndex = 1 AND XC) OR (AxisIndex = 2 AND XD) OR (AxisIndex = 3 AND XE) OR (AxisIndex = 4 AND XF);
        IF M20 OR M21 OR X8 OR (AxisMax >= 2 AND X9) OR (AxisMax >= 3 AND XA) OR (AxisMax >= 4 AND XB) THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            wBfm := 1500 + 100*(AxisIndex-1);
            TO(TRUE, wPrevStart, H0, wBfm, 1);
            AbsError := 901; SyncState := 90; SyncStep := 0;
        ELSIF bAxisStartDone AND NOT bAxisBusy THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            bAckTimerRestart := TRUE; bSyncTimerRestart := TRUE; SyncStep := 3;
        ELSIF SyncTimeoutQ THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            wBfm := 1500 + 100*(AxisIndex-1);
            TO(TRUE, wPrevStart, H0, wBfm, 1);
            AbsError := 905; SyncState := 90; SyncStep := 0;
        END_IF;
    ELSIF SyncStep = 3 THEN
        IF M20 OR M21 OR X8 OR (AxisMax >= 2 AND X9) OR (AxisMax >= 3 AND XA) OR (AxisMax >= 4 AND XB) THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            wBfm := 1500 + 100*(AxisIndex-1);
            TO(TRUE, wPrevStart, H0, wBfm, 1);
            AbsError := 901; SyncState := 90; SyncStep := 0;
        ELSIF SyncTimeoutQ THEN
            bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
            wBfm := 1500 + 100*(AxisIndex-1);
            TO(TRUE, wPrevStart, H0, wBfm, 1);
            AbsError := 906; SyncState := 90; SyncStep := 0;
        ELSIF AckTimerQ THEN
            wBfm := 800 + 100*(AxisIndex-1);
            FROM(TRUE, H0, wBfm, 1, wLoInt);
            FROM(TRUE, H0, wBfm + 1, 1, wHiInt);
            diRead := INT_TO_DINT(wHiInt) * 65536 + INT_TO_DINT(wLoInt);
            IF wLoInt < 0 THEN diRead := diRead + 65536; END_IF;
            IF diRead <> diQ[AxisIndex] THEN
                bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
                wBfm := 1500 + 100*(AxisIndex-1);
                TO(TRUE, wPrevStart, H0, wBfm, 1);
                AbsError := 906; SyncState := 90; SyncStep := 0;
            ELSE SyncStep := 4; END_IF;
        END_IF;
    ELSIF SyncStep = 4 THEN
        wBfm := 1500 + 100*(AxisIndex-1);
        TO(TRUE, wPrevStart, H0, wBfm, 1);
        SyncStep := 5;
    ELSIF SyncStep = 5 THEN
        bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
        IF AxisIndex < AxisMax THEN
            AxisIndex := AxisIndex + 1; bSyncTimerRestart := TRUE; SyncStep := 1;
        ELSE
            SyncState := 50; SyncStep := 0;
        END_IF;
    END_IF;
50:
    bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
    IF M20 OR M21 THEN AbsError := 901; SyncState := 90; SyncStep := 0; END_IF;
90:
    bM111 := FALSE; bM112 := FALSE; bM113 := FALSE; bM114 := FALSE;
    FOR iAxis := 1 TO 4 DO bAxisFresh[iAxis] := FALSE; END_FOR;
    ReadReq := FALSE;
END_CASE;
PositionValid := (SyncState = 50);
PositionValid1 := (SyncState = 50) AND bAxisFresh[1];
PositionValid2 := (SyncState = 50) AND bAxisFresh[2];
PositionValid3 := (SyncState = 50) AND bAxisFresh[3];
PositionValid4 := (SyncState = 50) AND bAxisFresh[4];
M111 := bM111;
M112 := bM112;
M113 := bM113;
M114 := bM114;

(* --- 4. CH2 RS-422 ENGINE: 02/91 ABS read only --- *)
StartEdge := ReadReq AND NOT PrevReadReq;
PrevReadReq := ReadReq;
TxFire := FALSE;
RxFire := FALSE;
WaitPreset := T#500ms;
IF State = 30 THEN WaitPreset := T#300ms; END_IF;
WaitTimer(IN := (State = PreviousState) AND ((State = 10) OR (State = 11) OR (State = 20) OR (State = 30) OR (State = 40) OR (State = 50)), PT := WaitPreset, Q := TimerExpired, ET := Elapsed);
PreviousState := State;
RetryTimer(IN := (State = 12) OR (State = 60), PT := T#100ms, Q := RetryExpired, ET := RetryElapsed);
IF NOT bReadEnable THEN
    Busy := FALSE; AbsValid := FALSE; AbsFresh := FALSE; ErrorCode := 1; State := 0;
ELSE
    IF StartEdge AND NOT Busy THEN
        IF NOT StartupReady THEN
            ErrorCode := 13;
        ELSE
            PositionValid := FALSE; ReadSuccess := FALSE; ServoAlarm := FALSE; AbsAlarm := FALSE;
            AbsValid := FALSE; AbsFresh := FALSE; ErrorCode := 0; LastError := 0;
            Attempt := 1; Count := 0; Busy := TRUE; State := 10;
        END_IF;
    END_IF;
    CASE State OF
    10:
        IF X8A OR X8B THEN
            RxFire := NOT RxDone[0];
        ELSIF NOT X89 AND NOT TxDone[0] AND NOT RxDone[0] THEN
            IF (U8\G610 <> 6) OR ((U8\G611 AND HFF3F) <> H070E) OR (U8\G310 <> 1) OR (U8\G324 <> 1) OR (U8\G325 <> HFFFF) OR
            (U8\G449 <> 0) OR (U8\G447 <> 0) OR (U8\G448 <> 0) OR ((U8\G307 AND H0300) <> 0) OR (U8\G333 <> 0) THEN
                ErrorCode := 2; State := 90;
            ELSE
                U8\G328 := 1; State := 11;
            END_IF;
        END_IF;
        IF TimerExpired THEN ErrorCode := 3; State := 90; END_IF;
    11:
        IF U8\G328 = 0 THEN State := 12; END_IF;
        IF TimerExpired THEN ErrorCode := 3; State := 90; END_IF;
    12:
        IF RetryExpired THEN
            IF (AbsStation < 0) OR (AbsStation > 9) THEN
                ErrorCode := 2; State := 90;
            ELSE
                Count := 0;
                FOR ByteIndex := 0 TO 31 DO Raw[ByteIndex] := 0; END_FOR;
                TxData[0] := INT_TO_WORD(12289 + AbsStation * 256);
                TxData[1] := H3230; TxData[2] := H3902; TxData[3] := H0331;
                ChecksumSum := WORD_TO_INT(SHR(TxData[0], 8) AND H00FF);
                FOR jLoop := 1 TO 3 DO
                    ChecksumSum := ChecksumSum + WORD_TO_INT(TxData[jLoop] AND H00FF) + WORD_TO_INT(SHR(TxData[jLoop], 8) AND H00FF);
                END_FOR;
                Nibble := ChecksumSum MOD 256;
                Ch := Nibble / 16;
                IF Ch < 10 THEN Ch := 48 + Ch; ELSE Ch := 55 + Ch; END_IF;
                TxData[4] := INT_TO_WORD(Ch);
                Ch := Nibble MOD 16;
                IF Ch < 10 THEN Ch := 48 + Ch; ELSE Ch := 55 + Ch; END_IF;
                TxData[4] := TxData[4] OR INT_TO_WORD(Ch * 256);
                TxControl[2] := 10; TxFire := TRUE; State := 20;
            END_IF;
        END_IF;
    20:
        IF TxDone[0] THEN
            IF TxDone[1] THEN ErrorCode := 4; State := 90; ELSE State := 30; END_IF;
        ELSIF TimerExpired THEN
            ErrorCode := 4; State := 90;
        END_IF;
    30:
        IF X8A OR X8B THEN RxFire := NOT RxDone[0]; END_IF;
        IF RxDone[0] THEN
            IF RxDone[1] OR (RxControl[2] < 1) OR (RxControl[2] > 32) THEN
                ErrorCode := 6; State := 40;
            ELSIF Count + RxControl[2] > 32 THEN
                ErrorCode := 7; State := 40;
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
                    IF Raw[Count - 3] = 3 THEN State := 35; END_IF;
                END_IF;
            END_IF;
        END_IF;
        IF TimerExpired AND (State = 30) THEN ErrorCode := 5; State := 40; END_IF;
    35:
        ErrorCode := 0;
        IF (Raw[0] <> 2) OR (Raw[1] <> (48 + AbsStation)) OR (Raw[Count - 3] <> 3) THEN
            ErrorCode := 8;
        ELSE
            ChecksumSum := 0;
            FOR ByteIndex := 1 TO Count - 3 DO ChecksumSum := ChecksumSum + Raw[ByteIndex]; END_FOR;
            ChecksumSum := ChecksumSum MOD 256;
            Expected := 0;
            FOR ByteIndex := Count - 2 TO Count - 1 DO
                Ch := Raw[ByteIndex];
                Nibble := -1;
                IF (Ch >= 48) AND (Ch <= 57) THEN Nibble := Ch - 48; END_IF;
                IF (Ch >= 65) AND (Ch <= 70) THEN Nibble := Ch - 55; END_IF;
                IF Nibble < 0 THEN ErrorCode := 10; ELSE Expected := Expected * 16 + Nibble; END_IF;
            END_FOR;
            IF (ErrorCode = 0) AND (Expected <> ChecksumSum) THEN ErrorCode := 9; END_IF;
            IF ErrorCode = 0 THEN
                IF Raw[2] = 97 THEN
                    ServoAlarm := TRUE; AbsAlarm := TRUE; ErrorCode := 12;
                ELSIF Raw[2] <> 65 THEN
                    ErrorCode := 11;
                ELSIF Count <> 14 THEN
                    ErrorCode := 8;
                ELSE
                    Accum := 0;
                    FOR ByteIndex := 3 TO 10 DO
                        Ch := Raw[ByteIndex];
                        Nibble := -1;
                        IF (Ch >= 48) AND (Ch <= 57) THEN Nibble := Ch - 48; END_IF;
                        IF (Ch >= 65) AND (Ch <= 70) THEN Nibble := Ch - 55; END_IF;
                        IF Nibble < 0 THEN
                            ErrorCode := 10;
                        ELSIF ByteIndex = 3 THEN
                            IF Nibble >= 8 THEN Accum := INT_TO_DINT(Nibble - 16); ELSE Accum := INT_TO_DINT(Nibble); END_IF;
                        ELSE
                            Accum := Accum * 16 + INT_TO_DINT(Nibble);
                        END_IF;
                    END_FOR;
                END_IF;
            END_IF;
        END_IF;
        IF ErrorCode = 0 THEN
            Position := Accum; ReadSuccess := TRUE; Busy := FALSE; State := 0;
            AbsPosition := Accum; AbsValid := TRUE; AbsFresh := TRUE; AbsAttempt := Attempt;
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
            ErrorCode := 4; State := 90;
        ELSIF NOT X89 AND NOT TxDone[0] AND NOT RxDone[0] THEN
            TxData[0] := H0004; TxControl[2] := 1; TxFire := TRUE; State := 50;
        END_IF;
    50:
        IF TxDone[0] AND (NOT TxDone[1]) THEN
            State := 60;
        ELSIF (TxDone[0] AND TxDone[1]) OR TimerExpired THEN
            ErrorCode := 4; State := 90;
        END_IF;
    60:
        IF RetryExpired THEN Attempt := Attempt + 1; State := 10; END_IF;
    90:
        LastError := ErrorCode; PositionValid := FALSE; Busy := FALSE; State := 0;
        AbsValid := FALSE; AbsFresh := FALSE;
        IF ErrorCode = 12 THEN AbsError := 902;
        ELSIF (ErrorCode >= 8) AND (ErrorCode <= 11) THEN AbsError := 908;
        ELSE AbsError := 902; END_IF;
        AbsAttempt := Attempt;
    END_CASE;
END_IF;
TxControl[0] := 2;
RxControl[0] := 2;
RxControl[3] := 16;
G_OUTPUT(TxFire, H8, TxControl, TxData[0], TxDone);
G_INPUT(RxFire, H8, RxControl, RxData[0], RxDone);
