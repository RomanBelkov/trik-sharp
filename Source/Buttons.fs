﻿namespace Trik
open System
open System.IO
open Trik.Collections

type ButtonPad (deviceFilePath) as self= 
    inherit Internals.BinaryFifoSensor<ButtonEvent>(deviceFilePath, 16, 1024)
    let mutable isDisposed = false
    let emulatorStream = new FileStream(deviceFilePath, FileMode.Open, FileAccess.Write, FileShare.Read)

    static let () = Enum.GetValues(typeof<ButtonEventCode>) 
                    |> Seq.cast<ButtonEventCode> 
                    |> Seq.tryFind  (fun x -> int x  < 0  || 255 < int x) 
                    |> Option.iter (fun x -> invalidArg (x.ToString()) "Button code out of bounds 0..255")
    
    do AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> (self :> IDisposable).Dispose())
    static let state = new Collections.BitArray(256)
    let testObserverDispose =  // All this magic is made for the first button press response time reducing  
        self.ToObservable()    // (the subscription and notification routine is mostly JITed and loaded at the construction time)
            .Subscribe(fun (x: ButtonEvent) -> 
              if x.Button <> ButtonEventCode.Sync then () 
              else printfn "%A" "starting")

    let mutable clicksOnly = true
    member self.ClicksOnly 
        with get() = clicksOnly
        and set f = clicksOnly <- f
        
    override self.Parse (bytes, offset) =
        //let evTime  = BitConverter.ToDouble(bytes, offset)
        let evType  = BitConverter.ToUInt16(bytes, offset + 8)
        let evCode  = BitConverter.ToUInt16(bytes, offset + 10)
        let evValue = BitConverter.ToInt32(bytes, offset + 12)
        if evType = 1us then 
            state.Set(int evCode, (evValue = 1))
            if evValue = 1 || not self.ClicksOnly then  
                Some <| ButtonEvent(evCode, (evValue = 1))//, evTime)
            else None
        else None

    ///returns you a bool ref by specified button event code. 
    ///It allows you to determine has specified button been pressed 
    member self.CheckPressing(button: ButtonEventCode) = 
        let isPressed = ref false
        let disp: IDisposable ref = ref null
        disp := self.ToObservable().Subscribe(fun (x: ButtonEvent) -> 
                       isPressed := button = x.Button && x.IsPressed
                       if !isPressed then (!disp).Dispose() 
                       )
        isPressed
    new () = new ButtonPad("/dev/input/event0")
    override self.Dispose() = 
        if not isDisposed then
            base.Dispose()
            let bytes = Emulations.buttonClick ButtonEventCode.Down 
            emulatorStream.Write(bytes, 0, bytes.Length) // Hack for the last key press emulation 
            emulatorStream.Flush(true)                   // (to make the program return from the last readline call). See BinaryFifoSensor's async read loop for details
            emulatorStream.Dispose()                     // This decision was made after trying to access INOTIFY throw mono interface  
            testObserverDispose.Dispose()                // And making attempts to pass cancelation token not just between binds in async. 
            isDisposed <- true
    
    override self.Finalize() = self.Dispose()

        