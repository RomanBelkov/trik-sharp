﻿module AnalogSensor

open System
open Extern
open Config
open System.Reactive.Linq

type AnalogSensor(number) = 
    let analogMin = 0
    let analogMax = 1024
    let getAnalogSensorRegister = int
    let limit l u (x :int) = Math.Min(u, Math.Max (l, x))  
    let register = getAnalogSensorRegister number
    let read() = 
        let value = Extern.receive register |> limit analogMin analogMax 
        (value - analogMin)
    let mutable obs:IObservable<_> = null            
    member this.Start() = 
        obs <- Observable.Generate(0, (fun _ -> true), (fun x -> x)
<<<<<<< HEAD
            , (fun _ -> read()), (fun _ -> System.TimeSpan.FromMilliseconds 1000.0 ))
=======
            , (fun _ -> read()), (fun _ -> System.TimeSpan.FromMilliseconds(1000.0)))
>>>>>>> 81888b28557c3b5a607e00d5a3c28083e37f186a
    member this.Obs = obs
    member this.Range = analogMax - analogMin