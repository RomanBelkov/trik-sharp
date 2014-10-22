﻿open Trik
open System.Threading

let maxAngle = 40
let minMass = 5
let deepPink = (255, 20, 147)

let mutable turnH = 0

let angleRotate x = (Trik.Helpers.limit (-maxAngle) maxAngle x) / 10

let changePos del = 
    match del with  
        | x when x > 10 && x < 90 && turnH < 90 -> turnH <- turnH + angleRotate x 
        | x when x < - 10 && x > -90 && turnH > -90 -> turnH <- turnH + angleRotate x  
        | _ -> ()

let HSVtoRGB (h, s, v) =
    if s = 0.0 then (v, v, v) 
    else
        let hs = h / 60.0
        let i = floor (hs)
        let f = hs - i
        let p = v * ( 1.0 - s )
        let q = v * ( 1.0 - s * f )
        let t = v * ( 1.0 - s * ( 1.0 - f ))
        match int i with
            | 0 -> (v, t, p)
            | 1 -> (q, v, p)
            | 2 -> (p, v, t)
            | 3 -> (p, q, v)
            | 4 -> (t, p, v)
            | _ -> (v, p, q)

let conversion (x : DetectTarget) = 
    let (r, g, b) = HSVtoRGB(float x.Hue, (float x.Saturation) / 100.0, (float x.Value) / 100.0)
    (int (r * 255.0), int (g * 255.0), int (b * 255.0))

[<EntryPoint>]
let main _ =
    let model = new Model(ObjectSensorConfig = Ports.VideoSource.USB)
    model.ServoConfig.[0] <- ("E1", "/sys/class/pwm/ehrpwm.1:1", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })
    Helpers.SendToShell """v4l2-ctl -d "/dev/video2" --set-ctrl white_balance_temperature_auto=1""" //option for better color reproduction

    let sensor = model.ObjectSensor
    let buttons = new ButtonPad()

    buttons.Start()
    sensor.Start()
    model.LedStripe.SetPower deepPink

    let sensorOutput = sensor.ToObservable()
    
    let targetStream = sensorOutput  
                       |> Observable.choose (fun o -> o.TryGetTarget) 

    let setterDisposable = targetStream 
                           |> Observable.subscribe sensor.SetDetectTarget

    let colorStream = targetStream 
                      |> Observable.map (fun x -> conversion x)

    let ledstripeDisposable = colorStream.Subscribe model.LedStripe

    let locationStream = sensorOutput  
                         |> Observable.choose (fun o -> o.TryGetLocation) 
                         |> Observable.map (fun o -> (changePos o.X, o.Mass))

    let motorDispose = locationStream.Subscribe (fun (_, y) -> if y > minMass then model.Servo.["E1"].SetPower (-turnH))

    use downButtonDispose = buttons.ToObservable() 
                            |> Observable.filter (fun x -> ButtonEventCode.Down = x.Button) 
                            |> Observable.subscribe (fun _ -> (*printfn "Detect by pressing Down";*) sensor.Detect())
    
    use upButtonDispose = buttons.ToObservable()
                            |> Observable.filter (fun x -> ButtonEventCode.Up = x.Button)
                            |> Observable.subscribe (fun _ -> (*printfn "Exiting...";*) System.Environment.Exit 0)

//    use timerSetterDisposable = Observable.Interval(System.TimeSpan.FromSeconds 7.0) 
//                                |> Observable.subscribe (fun _ -> (*printfn "Usual Detect by timer";*) sensor.Detect())

    Thread.Sleep Timeout.Infinite
    0