namespace LNTestFramework

open System.IO
open System.Reflection

module Utils =

    let getAssemblyDirectory () =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) |> Path.GetFullPath

