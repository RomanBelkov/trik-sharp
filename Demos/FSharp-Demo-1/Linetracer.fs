﻿module Linetracer

open System
open System.IO
open System.Collections
open System.Diagnostics
open System.Threading
open Trik
open Trik.Sensors
open Trik.Helpers


let loadIniConf path = 
    IO.File.ReadAllLines (path)
        |> Seq.choose(fun s -> 
            if s.Length > 0 then 
                let parts = 
                    s.Split ([| '=' |], StringSplitOptions.RemoveEmptyEntries) 
                    |> Array.map(fun s -> s.Trim([| ' '; '\r' |]) )
                Some(parts.[0], parts.[1]) 
            else None)
        |> dict

let logFifoPath = @"/tmp/dsp-detector.out.fifo"
let cmdFifoPath = @"/tmp/dsp-detector.in.fifo"

let speed = 100;
let stopK = 1;
let PK = 0.42;
let IK = 0.006;
let DK = -0.009;
let encC = 1.0 / (334.0 * 34.0); //1 : (num of points of encoder wheel * reductor ratio)
let max_fifo_input_size = 4000

let inline parse (s:string) = 
    let mutable n = 0
    let start = if s.Chars 0 |> Char.IsDigit then 0 else 1
    let sign = if s.Chars 0 = '-' then -1 else 1
    let zero = int '0'
    for i = start to s.Length - 1 do 
        n <- n * 10 + int (s.Chars i) - zero
    sign * n


type LogFifo(path:string) = 
    let sr = new StreamReader(path)

    let mutable loopDone = false
    let lineTargetDataParsed = new Event<_>()
    let lineColorDataParsed = new Event<_>()

    let checkLines (lines:string[]) last = 
        let mutable i = last
        let mutable wasLoc = false
        let mutable wasHsv = false
        while (not (wasHsv && wasLoc) ) && i >= 0 do
            let logStruct = lines.[i].Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
            //printfn "%s" logStruct.[0]
            if not wasLoc && logStruct.[0] = "loc:" then 
                let x     = parse logStruct.[1]
                let angle = parse logStruct.[2]
                let mass  = parse logStruct.[3]
                wasLoc <- true
                lineTargetDataParsed.Trigger(x, angle, mass)
                //printfn "ltparsed"
            elif not wasHsv && logStruct.[0] = "hsv:" then
                let hue    = parse logStruct.[1]
                let hueTol = parse logStruct.[2]
                let sat    = parse logStruct.[3]
                let satTol = parse logStruct.[4]
                let _val   = parse logStruct.[5]
                let valTol = parse logStruct.[6]
                wasHsv <- true
                lineColorDataParsed.Trigger(hue, hueTol, sat, satTol, _val, valTol)
            else ()
            i <- i - 1
    let rec loop() = 
        let ln = sr.ReadLine()
        //eprintfn "%s" ln
        checkLines [| ln |] 0
        if not loopDone then loop() 
    do Async.Start <| async { loop() } 
    member x.LineTargetDataParsed = lineTargetDataParsed.Publish
    member x.LineColorDataParsed = lineColorDataParsed.Publish
    interface IDisposable with
        member x.Dispose() = loopDone <- true

type CmdFifo(path:string) as self = 
    let fd = new IO.FileStream(path, FileMode.Truncate)
    let mutable h = null
    let detectTimer = new System.Timers.Timer(200.0, Enabled = false)
    do detectTimer.Elapsed.Add(fun _ -> self.Write("detect") )
    member x.Write(cmd:string) = 
        let buf = Text.Encoding.ASCII.GetBytes(cmd + "\n")
        fd.Write(buf, 0, buf.Length)
        fd.Flush()
    member x.Detect (logFifo:LogFifo) f = 
        h <- new Handler<_> (fun _ a -> 
            logFifo.LineColorDataParsed.RemoveHandler h
            detectTimer.Stop()
            eprintfn "x.Detect Handler"
            f a )
        logFifo.LineColorDataParsed.AddHandler(h)
        x.Write("detect")
        detectTimer.Start()
        eprintfn "x.Detect"


    interface IDisposable with
        member x.Dispose() = fd.Dispose()


type MotorControler (motor: Trik.Devices.PowerMotor, enc: Encoder, sign) = 
    [<Literal>]
    let max_ppms = 23.833f
    [<DefaultValue>]
    val mutable ActualSpeed : int
    [<DefaultValue>]
    val mutable PowerAddition : int
    [<DefaultValue>]
    val mutable PowerBase : int
    let mutable currentSpeed = 0.0f
    let mutable encPoints_prev = 0.0f
    let sw = new Stopwatch()
    do sw.Start()
    let mutable time_prev = sw.ElapsedMilliseconds
    let countSpeed() = 
        let parToRad = 0.03272492f
        let encPoints = parToRad * (float32 <| enc.Read())
        let time = sw.ElapsedMilliseconds
        let period = time - time_prev |> float32
        let speed = (encPoints - encPoints_prev) * 100.0f / (period * max_ppms)

        if currentSpeed <> speed then ()

        currentSpeed <- speed
        time_prev <- time
        encPoints_prev <- encPoints
        speed
    member x.doStep() = 
        x.ActualSpeed <- x.PowerBase + x.PowerAddition 
        motor.SetPower(sign * x.ActualSpeed)   

type Linetracer (model: Model) = 
    let sw = new Stopwatch()
    do eprintfn "Linetracer ctor"
    let cmd_fifo = new CmdFifo(cmdFifoPath)
    let log_fifo = new LogFifo(logFifoPath)
    //do System.Console.ReadKey |> ignore
    do sw.Restart()
    let localConfPath = "ltc.ini"
    let conf = loadIniConf localConfPath
    let elapsed = sw.ElapsedMilliseconds
    do eprintfn "Linetracer ctor: config parsed: %A ms" elapsed 
    let min_mass = conf.["min_mass"] |> parse
    let power_base = conf.["power_base"] |> parse
    let motor_sign = conf.["motor_sign"] |> parse
    let rl_sign = conf.["rl_sign"] |> parse
    let div_coefL = conf.["div_coefL"] |> Double.Parse
    let div_coefR = conf.["div_coefR"] |> Double.Parse
    let on_lost_coef = conf.["on_lost_coef"] |> Double.Parse
    let turn_mode_coef = conf.["turn_mode_coef"] |> parse
    let elapsed = sw.ElapsedMilliseconds
    do eprintfn "Linetracer ctor: config parsed: %A ms" elapsed 

    let motorL = new MotorControler(model.Motors.[M1], model.Encoders.[B2], motor_sign, PowerBase = power_base)
    let motorR = new MotorControler(model.Motors.[M3], model.Encoders.[B3], motor_sign * -1, PowerBase = power_base)
    let frontSensor = model.AnalogSensors.[A2]
    do eprintfn "Linetracer ctor: Motor controllers created"
    let mutable last_pow_add = 0
    let mutable (stopAutoMode: IDisposable) = null
    let mutable (frontSensorObs: IDisposable) = null
    let mutable (sideSensorObs: IDisposable) = null
    let startAutoMode(stm) = 
        (*frontSensorObs <- 
            frontSensor.ToObservable()
            |> Observable.subscribe (fun x -> if x > 60 then frontSensorObs.Dispose(); stm() ) *)
        stopAutoMode <-
            log_fifo.LineTargetDataParsed
            //|> Observable.subscribe(fun (x, angle, mass) -> eprintfn "lt: %d %d" x mass )
            //|> Observable.filter(fun (x, angle, mass) -> mass > min_mass )
            |> Observable.subscribe (fun (x, angle, mass) -> 
                if mass = 0 then
                    motorL.PowerAddition <- int <| float(last_pow_add) * div_coefL * on_lost_coef
                    motorR.PowerAddition <- int <| float(-last_pow_add) * div_coefR * on_lost_coef
                else 
                    last_pow_add <- x * rl_sign
                    motorL.PowerAddition <- int <| float(last_pow_add) * div_coefL
                    motorR.PowerAddition <- int <| float(-last_pow_add) * div_coefR
                //eprintfn "lt: %d %d" motorL.PowerAddition motorR.PowerAddition
                motorL.doStep()
                motorR.doStep()
                () )      
    let rec startTurnMode() = 
        eprintfn "startTurnMode"
        if stopAutoMode <> null then stopAutoMode.Dispose(); stopAutoMode <- null
        motorL.PowerAddition <- turn_mode_coef
        motorR.PowerAddition <- -turn_mode_coef
        Thread.Sleep 500
        motorL.PowerAddition <- -turn_mode_coef
        motorR.PowerAddition <- turn_mode_coef
        Thread.Sleep 500
        motorL.PowerAddition <- 0
        motorR.PowerAddition <- 0
        startAutoMode(startTurnMode)
    member x.Run() = 
        eprintfn "Linetracer.Run"
        cmd_fifo.Detect log_fifo <| fun (hue, hueTol, sat, satTol, _val, valTol) ->
            //printfn "detected: %d %d " hue hueTol
            printfn "detected"
            cmd_fifo.Write <| sprintf "hsv %d %d %d %d %d %d" hue hueTol sat satTol _val valTol
            startAutoMode(startTurnMode)
        eprintfn "Ready (any key to finish)"
        System.Console.ReadKey() |> ignore