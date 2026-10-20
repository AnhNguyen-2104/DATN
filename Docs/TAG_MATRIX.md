# MA TRAN TAG — DATN Line Control (TwinCAT ADS 851)
Legend: R = read-only on Dashboard; W = operator action only; RESERVED = planned (no PLC symbol).

## 1. TONG / MACHINE
| Symbol PLC | Direction | Purpose |
| MAIN.bSystemEStopOK | R | TRUE when auxiliary EStop feedback healthy |
| MAIN.bSystemStopRequested | R | Global normal PLC software stop request |
| GVL_MD200_IO.bEStopFeedback | R | ST-1228 slot 6 DI0 (input bit0) |
| MAIN.bPlcQConnected | R | PLC-Q Mitsubishi Ethernet MC socket |
| MAIN.bServo_Ready | R/Future | EziServo NC servo ready output |

## 2. BANG TAI CHUNG / ENC0
| MAIN.rConveyorSpeed | R | mm/s from ST-5114 encoder |
| MAIN.rConveyorDist | R | mm integrated distance since PLC reset |
| MAIN.bMD200_Start | R/W | MD200 run request; HMI START/normal STOP |
| MAIN.rMD200_SetFrequency_Hz | R/W | Hz setpoint limited 0..25 |
| MAIN.rMD200_Ramped_Hz | R | Ramp reference Hz |
| MAIN.rMD200_AO_VoltsVirtual | R | CALCULATED AO volts (not meter measurement) |
| GVL_MD200_IO.nAO1_Raw | R | ST-4424 slot11 AO1 0..4095 |
| GVL_MD200_IO.bRUN_DO | R | ST-2318 slot10 DO0 actual output image |

## 3. GANTRY / CAMERA 1
| MAIN.bVisionFeedVerified | R | Vision-to-PLC verification; not HMI Start |
| MAIN.nQueueCount | R | TwinCAT camera FIFO count |
| MAIN.nQueueHeadProductID | R | Head product ID |
| MAIN.nQueueAccepted | R | Accumulated queued products |
| MAIN.nQueueMissed | R | Missed product count |
| MAIN.bLegacyMotionPublisherAllow | R | Gate to legacy QD75 dispatcher, OFF by default |
| MAIN.bGlueCycleBusy | R | Robot execution state |
| MAIN.bGlueCycleDone | R | Completed 4-vertex operation |
| MAIN.bGlueCycleError | R | Gantry error status |
| MAIN.nGlueCurVertex | R | 1-4 vertex index |
| MAIN.nGlueErrorCode | R | Gantry diagnosis |
| MAIN.bStartGlueCycle | no HMI writing | QD75 motion; commissioning required |
| MAIN.bCameraTrigger / MAIN.rCameraCenterX,Y / MAIN.rCameraAngleDeg | vision bridge | Camera pose and trigger stage |
Contract on Mitsubishi: D100..D114; M1000 START held till M1001 ACK; M105 software cancel only.
Do not use M100/M101 (Mitsubishi homing); independent safety stop is hardwired.

## 4. SCARA / CAMERA 2 — RESERVED
Planned tags, NOT currently defined and NOT used for PLC writes:
GVL_SCARA.bConnected
GVL_SCARA.bReady
GVL_SCARA.bBusy
GVL_SCARA.bAlarm
GVL_SCARA.nActiveProductID
GVL_SCARA.nTraySlot
GVL_CAMERA2.bFrameValid
GVL_CAMERA2.rCenterX_mm / rCenterY_mm / rAngleDeg
GVL_CAMERA2.nQueueCount
Shared source: MAIN.rConveyorDist, MAIN.rConveyorSpeed
Future SCARA handshake must be separately audited before enabling.
