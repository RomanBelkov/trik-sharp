﻿namespace Trik.Junior

open System
open System.Collections.Generic
open Trik
type Robot() as is =
    
    static let mutable isRobotAlive = false
    do if isRobotAlive then invalidOp "Only single instance is allowed"
    do isRobotAlive <- true

    let super = new Trik.Model()
    let motors = ["M1"; "M2"; "M3"; "M4"] |> List.map super.Motor.get_Item
    let servos = ["E1"; "E2"] |> List.map super.Servo.get_Item
    let sensors = ["A1"; "A2"; "A3"] |> List.map super.AnalogSensor.get_Item

    let mutable gyroValue: Point = Point.Zero
    let mutable accelValue: Point = Point.Zero

    static let resources = new ResizeArray<_>()
    
    let mutable isDisposed = false
    do AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> if not isDisposed then (is :> IDisposable).Dispose())
    
    member self.Led 
        with get() =  super.Led.Color
        and set c = super.Led.Color <- c

    member self.MotorM1 with set p = motors.[0].Power <- p
    member self.MotorM2 with set p = motors.[1].Power <- p
    member self.MotorM3 with set p = motors.[2].Power <- p
    member self.MotorM4 with set p = motors.[3].Power <- p
    
    member self.SensorA1 = sensors.[0].Read()
    member self.SensorA2 = sensors.[1].Read()
    member self.SensorA3 = sensors.[2].Read()

    member self.ServoE1 with set p = servos.[0].Power <- p
    member self.ServoE2 with set p = servos.[1].Power <- p

    member self.GyroRead() = 
        match super.Gyro.BlockingRead() with
        | Some x -> lock gyroValue (fun () -> gyroValue <- x); x
        | None   -> gyroValue
    
    member self.AccelRead() = 
        match super.Gyro.BlockingRead() with
        | Some x -> lock gyroValue (fun () -> gyroValue <- x); x
        | None   -> gyroValue
    

    static member RegisterResource(d: IDisposable) = lock resources <| fun () -> resources.Add(d)

    member self.Stop() = motors |> List.iter (fun x -> x.Stop())
                         servos |> List.iter (fun x -> x.Zero())

    member self.Sleep(sec: float) = System.Threading.Thread.Sleep(int <| sec * 1000.)
    member self.Sleep(millisec: int) = System.Threading.Thread.Sleep(millisec)
    member self.Say(text) = Async.Start 
                            <| async { Trik.Helpers.SyscallShell <| "espeak -v russian_test -s 100 " + text}
    interface IDisposable with
        member self.Dispose() = 
            if not isDisposed then
                lock self 
                <| fun () -> 
                    isDisposed <- true
                    lock super 
                        <| fun () -> 
                        resources.ForEach(fun x -> x.Dispose())
                        (super :> IDisposable).Dispose()

    
[<AutoOpen>]
module Declarations =
    let robot = new Robot()