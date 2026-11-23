(* MR-J4-A command 02/data 91, station 0. OFFLINE BENCH ONLY.
   Two switches must both be TRUE: BenchEnable (bench isolation) and

   PollEnable (continuous poll master, default FALSE). Poll period is
   PollPeriod, default T#500ms. ManualRead rising edge still starts one read.
   No QD75, motion, servo-enable or safety output. No automatic polling when
   PollEnable is FALSE. A failed read in continuous mode is retried on the
   next PollTick after PollPeriod; LastError still records the failure.
   CH2 U8: nonprocedure, byte units, receive count=1, no delimiter,
   no conversion/transparent code; configure offline before later test.
   U8 = QJ71C24N at start XY 0080 (Un = 0080/16).
   CC-Link master QJ61BT11N at 0100 is U10 and is NOT used by this POU.
   Command 02/91 also sets the amplifier internal position-command value.
   Error: 1 disabled, 2 configuration, 3 stale/clear, 4 TX,
   5 timeout, 6 RX, 7 overflow, 8 frame, 9 checksum, 10 hex,
   11 negative status, 12 servo alarm, 13 startup guard. LastError survives retry. *)
PollTimer(IN := PollEnable AND NOT Busy AND (State = 0), PT := PollPeriod, Q := PollTick);
StartEdge := (ManualRead AND NOT PrevRead) OR PollTick;
PrevRead := ManualRead;
TxFire := FALSE;
RxFire := FALSE;
IF PollPeriod <= T#0ms THEN PollPeriod := T#500ms; END_IF;
IF PollEnable AND NOT PrevPollEnable THEN PollCount := 0; END_IF;
PrevPollEnable := PollEnable;
ReadyTimer(IN := BenchEnable, PT := T#3500ms, Q := StartupReady);
WaitPreset := T#500ms;
IF State=30 THEN WaitPreset:=T#300ms; END_IF;
WaitTimer(IN := (State=PreviousState) AND ((State=10) OR (State=11) OR
 (State=20) OR (State=30) OR (State=40) OR (State=50)),
 PT := WaitPreset, Q := TimerExpired, ET := Elapsed);
PreviousState := State;
RetryTimer(IN := (State=12) OR (State=60), PT := T#100ms,
 Q := RetryExpired, ET := RetryElapsed);
IF ResetResult AND NOT Busy THEN
 ReadSuccess := FALSE;
 ErrorCode := 0;
 LastError := 0;
 IF NOT PrevResetResult THEN PollCount := 0; END_IF;
END_IF;
PrevResetResult := ResetResult;
IF NOT BenchEnable THEN
 PositionValid := FALSE;
 Busy := FALSE;
 ReadSuccess := FALSE;
 ErrorCode := 1;
 State := 0;
ELSE
 IF StartEdge AND NOT Busy AND NOT StartupReady THEN ErrorCode:=13; END_IF;
 IF StartEdge AND NOT Busy AND StartupReady THEN
  PositionValid := FALSE;
  ReadSuccess := FALSE;
  ServoAlarm := FALSE;
  ErrorCode := 0;
  LastError := 0;
  Attempt := 1;
  Count := 0;
  Busy := TRUE;
  State := 10;
  IF PollTick THEN PollCount := PollCount + 1; END_IF;
 END_IF;
 CASE State OF
 10:
  (* Drain old complete data before receive-clear. Dedicated calls own CH2 only. *)
  IF X8A OR X8B THEN
   RxFire := NOT RxDone[0];
  ELSIF NOT X89 AND NOT TxDone[0] AND NOT RxDone[0] THEN
   IF (U8\G610<>6) OR ((U8\G611 AND HFF3F)<>H070E) OR
    (U8\G310<>1) OR (U8\G324<>1) OR (U8\G325<>HFFFF) OR
    (U8\G449<>0) OR (U8\G447<>0) OR (U8\G448<>0) OR
    ((U8\G307 AND H0300)<>0) OR (U8\G333<>0) THEN
    ErrorCode := 2;
    State := 90;
   ELSE
    U8\G328 := 1;
    State := 11;
   END_IF;
  END_IF;
  IF TimerExpired THEN ErrorCode := 3; State := 90; END_IF;
 11:
  IF U8\G328=0 THEN State := 12; END_IF;
  IF TimerExpired THEN ErrorCode := 3; State := 90; END_IF;
 12:
  IF RetryExpired THEN
   Count := 0;
   FOR ByteIndex:=0 TO 31 DO Raw[ByteIndex]:=0; END_FOR;
   TxData[0]:=H3001; TxData[1]:=H3230; TxData[2]:=H3902;
   TxData[3]:=H0331; TxData[4]:=H3130;
   TxControl[2]:=10;
   TxFire:=TRUE;
   State:=20;
  END_IF;
 20:
  IF TxDone[0] THEN
   IF TxDone[1] THEN ErrorCode:=4; State:=90;
   ELSE State:=30; END_IF;
  ELSIF TimerExpired THEN ErrorCode:=4; State:=90;
  END_IF;
 30:
  IF X8A OR X8B THEN RxFire:=NOT RxDone[0]; END_IF;
  IF RxDone[0] THEN
   IF RxDone[1] OR (RxControl[2]<1) OR (RxControl[2]>32) THEN
    ErrorCode:=6; State:=40;
   ELSIF Count+RxControl[2]>32 THEN ErrorCode:=7; State:=40;
   ELSE
    FOR ByteIndex:=0 TO RxControl[2]-1 DO
     IF (ByteIndex MOD 2)=0 THEN
      Raw[Count]:=WORD_TO_INT(RxData[ByteIndex/2] AND H00FF);
     ELSE
      Raw[Count]:=WORD_TO_INT(SHR(RxData[ByteIndex/2],8) AND H00FF);
     END_IF;
     Count:=Count+1;
    END_FOR;
    IF Count>=6 THEN
     IF Raw[Count-3]=3 THEN State:=35; END_IF;
    END_IF;
   END_IF;
  END_IF;
  IF TimerExpired AND (State=30) THEN ErrorCode:=5; State:=40; END_IF;
 35:
  ErrorCode:=0;
  IF (Raw[0]<>2) OR (Raw[1]<>48) OR (Raw[Count-3]<>3) THEN
   ErrorCode:=8;
  ELSE
   ChecksumSum:=0;
   FOR ByteIndex:=1 TO Count-3 DO ChecksumSum:=ChecksumSum+Raw[ByteIndex]; END_FOR;
   ChecksumSum:=ChecksumSum MOD 256;
   Expected:=0;
   FOR ByteIndex:=Count-2 TO Count-1 DO
    Ch:=Raw[ByteIndex]; Nibble:=-1;
    IF (Ch>=48) AND (Ch<=57) THEN Nibble:=Ch-48; END_IF;
    IF (Ch>=65) AND (Ch<=70) THEN Nibble:=Ch-55; END_IF;
    IF Nibble<0 THEN ErrorCode:=10;
    ELSE Expected:=Expected*16+Nibble; END_IF;
   END_FOR;
   IF (ErrorCode=0) AND (Expected<>ChecksumSum) THEN ErrorCode:=9; END_IF;
   IF ErrorCode=0 THEN
    IF Raw[2]=97 THEN ServoAlarm:=TRUE; ErrorCode:=12;
    ELSIF Raw[2]<>65 THEN ErrorCode:=11;
    ELSIF Count<>14 THEN ErrorCode:=8;
    ELSE
     Accum:=0;
     FOR ByteIndex:=3 TO 10 DO
      Ch:=Raw[ByteIndex]; Nibble:=-1;
      IF (Ch>=48) AND (Ch<=57) THEN Nibble:=Ch-48; END_IF;
      IF (Ch>=65) AND (Ch<=70) THEN Nibble:=Ch-55; END_IF;
      IF Nibble<0 THEN ErrorCode:=10;
      ELSE
       IF ByteIndex=3 THEN
        IF Nibble>=8 THEN Accum:=INT_TO_DINT(Nibble-16);
        ELSE Accum:=INT_TO_DINT(Nibble); END_IF;
       ELSE Accum:=Accum*16+INT_TO_DINT(Nibble); END_IF;
      END_IF;
     END_FOR;
    END_IF;
   END_IF;
  END_IF;
  IF ErrorCode=0 THEN
   Position:=Accum; PositionValid:=TRUE; ReadSuccess:=TRUE;
   Busy:=FALSE; State:=0;
  ELSIF ErrorCode=12 THEN State:=90;
  ELSE State:=40; END_IF;
 40:
  LastError:=ErrorCode;
  IF Attempt>=3 THEN State:=90;
  ELSIF TimerExpired THEN ErrorCode:=4; State:=90;
  ELSIF NOT X89 AND NOT TxDone[0] AND NOT RxDone[0] THEN
   (* EOT then wait >=100ms before retry. Never clear during TX. *)
   TxData[0]:=H0004; TxControl[2]:=1;
   TxFire:=TRUE; State:=50;
  END_IF;
 50:
  IF TxDone[0] THEN
   IF TxDone[1] THEN ErrorCode:=4; State:=90;
   ELSE State:=60; END_IF;
  ELSIF TimerExpired THEN ErrorCode:=4; State:=90; END_IF;
 60:
  IF RetryExpired THEN Attempt:=Attempt+1; State:=10; END_IF;
 90:
  LastError:=ErrorCode; PositionValid:=FALSE; Busy:=FALSE; State:=0;
 END_CASE;
END_IF;
TxControl[0]:=2;
RxControl[0]:=2;
RxControl[3]:=16;
G_OUTPUT(TxFire, H8, TxControl, TxData[0], TxDone);
G_INPUT(RxFire, H8, RxControl, RxData[0], RxDone);
