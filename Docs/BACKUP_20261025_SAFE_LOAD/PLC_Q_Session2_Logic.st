(***************************************************************************************************)
(* PROGRAM : Tracking (ST) - Q06UDEH + QD75D4                                                      *)
(* VERSION : 4.0 - TAT CA LENH 32-BIT DUNG DMOV(TRUE, Nguon, Dich)                                 *)
(* KIEU    : TwinCAT ghi D -> DMOV D->VAR -> tinh toan tren VAR -> DMOV VAR->D -> DTO nap QD75    *)
(* QUY TAC : Single-Writer | Y0 thuoc MAIN | Y10 owner MAIN qua M110 | 0 Double-Coil               *)
(* TAG     : Local Label KHONG gan Device (Device de trong), chi khai Class + Data Type            *)
(***************************************************************************************************)

(***************************************************************************************************)
(* 1. KHOI TAO                                                                                     *)
(***************************************************************************************************)
IF SM402 OR NOT bInit THEN
    DMOV(TRUE, 0, iState);                  (* iState = 0 (D200-D201 hoac noi bo) *)
    DMOV(TRUE, 0, dErrCode);                (* dErrCode = 0 *)
    DMOV(TRUE, 0, Target_X_Sync);           (* X cuoi Point 2/3 = 0 *)
    bReq_Start_Y10 := FALSE;
    bMCode_Handled := FALSE;
    bValve_Open    := FALSE;
    bM100_Old      := FALSE;
    bInit          := TRUE;
END_IF;

(***************************************************************************************************)
(* 2. DAY TRANG THAI RA THANH GHI D CHO TWINCAT DOC                                                *)
(***************************************************************************************************)
DMOV(TRUE, iState,        D200);            (* D200-D201 : buoc FSM *)
DMOV(TRUE, dErrCode,      D120);            (* D120-D121 : ma loi phan mem *)
DMOV(TRUE, Target_X_Sync, D116);            (* D116-D117 : X ket thuc truot *)

bQd75Ready := X0;

(***************************************************************************************************)
(* 3. DOC MA LOI QD75 (Md.23) - ghi truc tiep vao device D122/D124/D126                            *)
(***************************************************************************************************)
FROM(TRUE, H0, 806,  1, D122);              (* D122 : ma loi truc 1 *)
FROM(TRUE, H0, 906,  1, D124);              (* D124 : ma loi truc 2 *)
FROM(TRUE, H0, 1006, 1, D126);              (* D126 : ma loi truc 3 *)

(***************************************************************************************************)
(* 4. DUNG KHAN CAP / HUY CHU TRINH (M105)                                                         *)
(***************************************************************************************************)
IF M105 THEN
    DMOV(TRUE, 0, iState);
    bReq_Start_Y10 := FALSE;
    bMCode_Handled := FALSE;
    bValve_Open    := FALSE;
    IF X8 OR X9 OR XA THEN
        TO(TRUE, 1, H0, 1502, 1);           (* Cd.5 truc 1 : xoa loi *)
        TO(TRUE, 1, H0, 1602, 1);           (* Cd.5 truc 2 : xoa loi *)
        TO(TRUE, 1, H0, 1702, 1);           (* Cd.5 truc 3 : xoa loi *)
        DMOV(TRUE, 0, dErrCode);
    END_IF;
END_IF;

(***************************************************************************************************)
(* 5. MAY TRANG THAI FSM (iState)                                                                  *)
(***************************************************************************************************)
CASE iState OF

    0:
        bReq_Start_Y10 := FALSE;

        IF M1000 AND NOT bM100_Old THEN
            IF X0 AND NOT XC AND NOT XD AND NOT XE AND NOT X8 AND NOT X9 AND NOT XA AND NOT M105 THEN
                DMOV(TRUE, 10, iState);
                DMOV(TRUE, 0, dErrCode);
            ELSE
                DMOV(TRUE, 901, dErrCode);  (* 901 : QD75 chua san sang hoac dang ban *)
            END_IF;
        END_IF;

    10:
        (* 10.1 Doc du lieu TwinCAT tu cac thanh ghi D *)
        DMOV(TRUE, D100, Target_X);
        DMOV(TRUE, D102, Target_Y);
        DMOV(TRUE, D104, Target_Z_Down);
        DMOV(TRUE, D106, Speed_Approach);
        DMOV(TRUE, D108, Speed_Sync);
        DMOV(TRUE, D110, DeltaX_Sync);
        DMOV(TRUE, D112, Target_Z_Safe);

        (* 10.2 Tinh toa do ket thuc truot va phat ra D116 *)
        Target_X_Sync := Target_X + DeltaX_Sync;
        DMOV(TRUE, Target_X_Sync, D116);

        (* 10.3 Kiem tra gioi han hanh trinh mem *)
        IF (Target_X < 0) OR (Target_X > 1800000) OR (Target_X_Sync < 0) OR (Target_X_Sync > 1800000) THEN
            DMOV(TRUE, 501, dErrCode);      (* 501 : X vuot 180 mm *)
            DMOV(TRUE, 0, iState);
        ELSIF (Target_Y < 0) OR (Target_Y > 1700000) THEN
            DMOV(TRUE, 502, dErrCode);      (* 502 : Y vuot 170 mm *)
            DMOV(TRUE, 0, iState);
        ELSIF (Target_Z_Down < 0) OR (Target_Z_Down > 520000) OR (Target_Z_Safe < 0) OR (Target_Z_Safe > 520000) OR (Target_Z_Down < Target_Z_Safe) THEN
            DMOV(TRUE, 503, dErrCode);      (* 503 : Z khong hop le *)
            DMOV(TRUE, 0, iState);
        ELSE
            (* ================= POINT 1 : BAY TIEP CAN DINH (1503H) ================= *)
            TO (TRUE, 16#1503, H0, 2000, 1);    (* Identifier = ABS3 + Continuous Path *)
            TO (TRUE, 0, H0, 2001, 1);          (* M-Code = 0 *)
            DTO(TRUE, 0,    H0, 2002, 1);       (* Dwell = 0 *)
            DTO(TRUE, D106, H0, 2004, 1);       (* Speed_Approach *)
            DTO(TRUE, D100, H0, 2006, 1);       (* X = Target_X *)
            DTO(TRUE, D102, H0, 8006, 1);       (* Y = Target_Y *)
            DTO(TRUE, D104, H0, 14006, 1);      (* Z = Target_Z_Down *)

            (* ================= POINT 2 : TRUOT DONG TOC + XA KEO 30MS ============== *)
            TO (TRUE, 16#1503, H0, 2010, 1);    (* Identifier = ABS3 + Continuous Path *)
            TO (TRUE, 1, H0, 2011, 1);          (* M-Code = 1 -> bat X4 tai Point 2 *)
            DTO(TRUE, 0,    H0, 2012, 1);       (* Dwell = 0 *)
            DTO(TRUE, D108, H0, 2014, 1);       (* Speed_Sync *)
            DTO(TRUE, D116, H0, 2016, 1);       (* X cuoi = Target_X + DeltaX_Sync *)
            DTO(TRUE, D102, H0, 8016, 1);       (* Y giu nguyen *)
            DTO(TRUE, D104, H0, 14016, 1);      (* Z giu nguyen *)

            (* ================= POINT 3 : RUT Z AN TOAN (1500H) ===================== *)
            TO (TRUE, 16#1500, H0, 2020, 1);    (* Identifier = ABS3 + Positioning Complete *)
            TO (TRUE, 0, H0, 2021, 1);          (* M-Code = 0 *)
            DTO(TRUE, 0,    H0, 2022, 1);       (* Dwell = 0 *)
            DTO(TRUE, D106, H0, 2024, 1);       (* Speed_Approach *)
            DTO(TRUE, D116, H0, 2026, 1);       (* X giu nguyen *)
            DTO(TRUE, D102, H0, 8026, 1);       (* Y giu nguyen *)
            DTO(TRUE, D112, H0, 14026, 1);      (* Z = Target_Z_Safe *)

            TO(TRUE, 1, H0, 1500, 1);           (* Cd.3 = 1 : bat dau tu Point 1 *)

            DMOV(TRUE, 20, iState);
        END_IF;

    (***********************************************************************************************)
    (* STATE 20 : KICH PHAT XUNG Y10 QUA M110 - CHO X10 (START COMPLETE)                            *)
    (***********************************************************************************************)
    20:
        bReq_Start_Y10 := TRUE;

        IF X10 THEN
            bReq_Start_Y10 := FALSE;
            DMOV(TRUE, 30, iState);
        END_IF;

    (***********************************************************************************************)
    (* STATE 30 : DANG CHAY 3 POINT - XU LY M-CODE X4 (POINT 2)                                     *)
    (***********************************************************************************************)
    30:
        IF X4 AND NOT bMCode_Handled THEN
            bMCode_Handled := TRUE;
            bValve_Open    := TRUE;             (* Bat dau xung mo van 30ms *)
            TO(TRUE, 1, H0, 1504, 1);           (* Cd.7 = 1 : yeu cau tat M-Code *)
        END_IF;

        IF NOT X4 THEN
            TO(TRUE, 0, H0, 1504, 1);           (* Cd.7 = 0 *)
        END_IF;

        IF X14 AND NOT XC AND NOT XD AND NOT XE THEN
            bMCode_Handled := FALSE;
            bValve_Open    := FALSE;
            DMOV(TRUE, 40, iState);
        END_IF;

    (***********************************************************************************************)
    (* STATE 40 : HOAN THANH DINH - BAT ACK                                                         *)
    (***********************************************************************************************)
    40:
        DMOV(TRUE, 50, iState);

    (***********************************************************************************************)
    (* STATE 50 : CHO TWINCAT HA M1000                                                              *)
    (***********************************************************************************************)
    50:
        IF NOT M1000 THEN
            DMOV(TRUE, 0, iState);
        END_IF;

END_CASE;

(***************************************************************************************************)
(* 6. DINH THI VAN KEO 30MS (TIMER TOC DO CAO)                                                     *)
(***************************************************************************************************)
bValve_Timer_Run := bValve_Open AND NOT M105;   (* Timer chi chay khi xung mo van dang hieu luc *)
TIMER_H_M(bValve_Timer_Run, TC0, 3);            (* 3 x 10ms = 30ms *)

IF TS0 THEN
    bValve_Open := FALSE;                       (* Ket thuc xung: dong van, khong tu mo lai *)
END_IF;

(***************************************************************************************************)
(* 7. TANG XUAT BIT DUY NHAT (MOI NGO RA CHI GAN DUNG 1 LAN)                                       *)
(***************************************************************************************************)
M102  := (iState >= 10) AND (iState <= 30) AND NOT M105;    (* Robot_Busy *)
M103  := (iState = 40) OR (iState = 50);                    (* Robot_In_Position *)
M1001 := (iState = 40) OR (iState = 50);                    (* Ack_Vertex_Done *)
M104  := X8 OR X9 OR XA OR (dErrCode <> 0);                 (* Robot_Alarm *)

Y4 := M105;                                 (* Axis 1 Stop *)
Y5 := M105;                                 (* Axis 2 Stop *)
Y6 := M105;                                 (* Axis 3 Stop *)

M110 := bReq_Start_Y10 AND NOT M105;        (* Yeu cau Start -> MAIN xuat Y10 *)

Y20 := bValve_Open AND NOT M105;            (* Van keo vat ly (Slot 1 QY40P) *)

(***************************************************************************************************)
(* 8. LUU LICH SU TIN HIEU DE BAT CANH LEN                                                         *)
(***************************************************************************************************)
bM100_Old := M1000;
