﻿open System
open Trik
[<EntryPoint>]
let main argv = 
    Helpers.I2C.Init "/dev/i2c-2" 0x48 1
    use model = new Model()
//model.LedStripe |> ignore
    let inline f coord = 
        (100 - abs(coord.x/10) - abs(coord.z/10), abs(coord.x/10) + abs(coord.y/10), abs(coord.z/10) + abs(coord.y/10))  
    use disp = model.Gyro.ToObservable().Subscribe(
                     fun coord -> model.LedStripe.SetPower(f coord))

    Console.ReadKey() |> ignore
    0 // return an integer exit code
