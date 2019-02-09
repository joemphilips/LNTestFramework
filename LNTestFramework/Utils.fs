namespace LNTestFramework

open System.Reflection
open System
open System.IO

module Utils =
    let getAssemblyDirectory () =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) |> Path.GetFullPath
