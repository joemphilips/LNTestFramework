namespace LNTestFramework

open System.Threading
open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading.Tasks
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open Microsoft.FSharp.Reflection
open BTCPayServer.Lightning.LND
open BTCPayServer.Lightning
open NBitcoin
open NBitcoin.RPC
open LNTestFramework.Utils

type LauncherSettings = {
  NETWORK: string
  BITCOIND_RPCPORT: int
  BITCOIND_PORT: int
  DATADIR: string
  BALANCER_RESTPORT: int
  CUSTODY_RESTPORT: int
  THIRDPARTY_RESTPORT: int
}

type Clients = {
  Bitcoin: NBitcoin.RPC.RPCClient
  Rebalancer: LndClient
  Custody: ILightningClient
  ThirdParty: ILightningClient
} with member x.GetAll() = seq { yield (x.Rebalancer :> ILightningClient); yield x.Custody; yield x.ThirdParty }

[<AutoOpen>]
module LightningNodeLauncher =
    // Wrapper for the specific process of docker-compose
    type LightningNodeBuilder(name: string, network: Network, composeFilePath: string) =
        let checkConnection portN =
            let l = new TcpListener(IPAddress.Loopback, portN)
            l.Start()
            l.Stop()

        let findEmptyPort (number: int) =
            let mutable i = 0
            let mutable result = Array.create number 0
            while (i < number) do
                let mutable port = RandomUtils.GetInt32() % 4000
                port <- port + 10000
                if not (result |> Array.contains port) then
                    try
                        checkConnection port
                        result.[i] <- port
                        i <- i + 1
                    with
                    | :? SocketException -> ()          
            result

        let ports = findEmptyPort 5
        let settings = {
            NETWORK = network.ToString().ToLower()
            BITCOIND_PORT = ports.[0]
            BITCOIND_RPCPORT = ports.[1]
            DATADIR = name
            BALANCER_RESTPORT = ports.[2]
            CUSTODY_RESTPORT = ports.[3]
            THIRDPARTY_RESTPORT = ports.[4]
        }

        let mutable _Process: Process = null

        let convertSettingsToEnvInStartInfo (settings: LauncherSettings): ProcessStartInfo =
            let mutable startInfo = new ProcessStartInfo()
            let allFields = FSharpType.GetRecordFields (settings.GetType())
            for k in allFields do
                startInfo.EnvironmentVariables.["LNLAUNCHER_" + k.Name] <- k.GetValue(settings).ToString()
            startInfo

        let rec checkConnected (clients: Clients) =
            async {
                do! Async.Sleep(2000)
                try
                    Console.WriteLine("checking connection ...")
                    let _ = clients.GetAll() |> Seq.map(fun c -> c.GetInfo())|> Task.WhenAll |> Async.AwaitTask
                    ()
                with
                | :? SocketException -> return! checkConnected clients
                | :? AggregateException -> return! checkConnected clients
            }

        let runDockerComposeDown() =
            let startInfo = convertSettingsToEnvInStartInfo settings
            startInfo.EnvironmentVariables.["COMPOSE_PROJECT_NAME"] <- name
            startInfo.FileName <- "docker-compose"
            startInfo.Arguments <- " -f " + composeFilePath + " down"
            let p = Process.Start(startInfo)
            p.WaitForExit()
            ()

        let asyncWaitLnSynced (bitcoin: RPCClient) (lnClient: ILightningClient) =
            async {
                let! lnInfo = lnClient.GetInfo() |> Async.AwaitTask
                let! height = bitcoin.GetBlockCountAsync() |> Async.AwaitTask
                let mutable shouldWait = (lnInfo.BlockHeight < height)
                while shouldWait do
                    let! lnInfo = lnClient.GetInfo() |> Async.AwaitTask
                    let! height = bitcoin.GetBlockCountAsync() |> Async.AwaitTask
                    shouldWait <- (lnInfo.BlockHeight < height)
                    printf "height in bitcoin is %s and height in lnd is %s \n" (height.ToString()) (lnInfo.BlockHeight.ToString())
                    do! Async.Sleep(500)
                return ()
            }

        let WaitLnSyncedAsync (bitcoin: RPCClient, lnClient: ILightningClient) =
            (asyncWaitLnSynced bitcoin lnClient |> Async.StartAsTask).ConfigureAwait(false)

        let WaitLnSynced (bitcoin: RPCClient, lnClient: ILightningClient) =
            asyncWaitLnSynced bitcoin lnClient |> Async.RunSynchronously

        interface IDisposable with
            member this.Dispose() =
                if _Process <> null && not _Process.HasExited then
                    runDockerComposeDown()
                    if not _Process.HasExited then
                        _Process.Kill()
                        _Process.WaitForExit()
                        _Process.Dispose()
                        _Process <- null
                    printfn "disposed Builder %s " name

        member this.asyncStartNode() =
            async {
                let startInfo = convertSettingsToEnvInStartInfo settings
                startInfo.EnvironmentVariables.["COMPOSE_PROJECT_NAME"] <- name
                startInfo.FileName <- "docker-compose"
                startInfo.Arguments <- " -f " + composeFilePath + " up"
                // startInfo.ArgumentList.Add("-d")
                startInfo.ErrorDialog <- true
                startInfo.RedirectStandardError <- true
                startInfo.RedirectStandardOutput <- true
                _Process <- Process.Start(startInfo)
                // printf "%s" (p.StandardError.ReadToEnd())
                let c = this.GetClients()
                do! c |> checkConnected
                // lnd requires at least one block in the chain. (otherwise it will return 500 when tried to connect)
                c.Bitcoin.Generate(1) |> ignore
                // end we must wait until lnd can scan that block.
                do! Async.Sleep 1000
                ()
            }

        member this.startNode() =
            this.asyncStartNode() |> Async.RunSynchronously


        member this.GetClients(): Clients =
            let fac = new LightningClientFactory(network)

            let con1 = sprintf "type=lnd-rest;server=https://lnd:lnd@127.0.0.1:%d;allowinsecure=true" settings.BALANCER_RESTPORT
            {
                Bitcoin = new RPCClient(RPCCredentialString.Parse("0I5rfLbJEXsg:yJt7h7D8JpQy"),
                                        new Uri(sprintf "http://localhost:%d" settings.BITCOIND_RPCPORT),
                                        network)
                Rebalancer = (fac.Create(con1) :?> LndClient)
                Custody = fac.Create(sprintf "type=lnd-rest;server=https://lnd:lnd@127.0.0.1:%d;allowinsecure=true" settings.CUSTODY_RESTPORT)
                ThirdParty = fac.Create(sprintf "type=lnd-rest;server=https://lnd:lnd@127.0.0.1:%d;allowinsecure=true" settings.THIRDPARTY_RESTPORT)
            }

        member this.asyncConnect(from: ILightningClient, dest: ILightningClient) =
            async {
                let! info = dest.GetInfo() |> Async.AwaitTask
                do! from.ConnectTo(info.NodeInfo) |> Async.AwaitTask
                return ()
            }

         member this.ConnectAsync(from: ILightningClient, dest: ILightningClient) =
             (this.asyncConnect(from, dest) |> Async.StartAsTask).ConfigureAwait(false)

         member this.Connect(from: ILightningClient, dest: ILightningClient) =
             this.ConnectAsync(from, dest).GetAwaiter().GetResult()

         member this.asyncConnectAll() =
             let clients = this.GetClients()
             [|
                 this.asyncConnect(clients.Rebalancer, clients.ThirdParty)
                 this.asyncConnect(clients.Rebalancer, clients.Custody)
                 this.asyncConnect(clients.Custody, clients.ThirdParty)
             |] |> Async.Parallel

         member this.ConnectAllAsync() = 
             (this.asyncConnectAll() |> Async.StartAsTask).ConfigureAwait(false)

         member this.ConnectAll() =
             this.ConnectAllAsync().GetAwaiter().GetResult()


        member this.asyncOpenChannel(cashCow: RPCClient, from: ILightningClient, dest: ILightningClient, amount: Money) =
            async {
                let! destInvoice = dest.CreateInvoice(LightMoney.op_Implicit(1000), "EnsureConnectedToDest", TimeSpan.FromSeconds(5000.0)) |> Async.AwaitTask
                let! info = dest.GetInfo() |> Async.AwaitTask
                let request = new OpenChannelRequest()
                request.NodeInfo <- info.NodeInfo
                request.ChannelAmount <- amount
                request.FeeRate <- new NBitcoin.FeeRate(0.0004m)
                let! payResult = from.Pay(destInvoice.BOLT11) |> Async.AwaitTask
                let mutable notOpened = payResult.Result = PayResult.CouldNotFindRoute
                while notOpened do
                    Console.WriteLine("Openning channel ...")
                    let! response = from.OpenChannel(request)|> Async.AwaitTask
                    if response.Result = OpenChannelResult.CannotAffordFunding then
                        Console.WriteLine("Cannot afford fund")
                        do! this.PrepareBTCFundsAsync()
                        let! addr = from.GetDepositAddress()|> Async.AwaitTask
                        let! _ = cashCow.SendToAddressAsync(addr, Money.Coins(0.1m))|> Async.AwaitTask
                        let! _ = cashCow.GenerateAsync(10)|> Async.AwaitTask
                        do! asyncWaitLnSynced cashCow from
                        do! asyncWaitLnSynced cashCow dest
                    if response.Result = OpenChannelResult.PeerNotConnected then
                        Console.WriteLine("Peer not conn")
                        do! from.ConnectTo(info.NodeInfo) |> Async.AwaitTask
                    if response.Result = OpenChannelResult.NeedMoreConf then
                        Console.WriteLine("Need more conf")
                        let! _ = cashCow.GenerateAsync(6) |> Async.AwaitTask
                        do! asyncWaitLnSynced cashCow from
                        do! asyncWaitLnSynced cashCow dest
                    if response.Result = OpenChannelResult.AlreadyExists then 
                        Console.WriteLine("already exists")
                        do! Async.Sleep(1000) 
                    let! payResult = from.Pay(destInvoice.BOLT11) |> Async.AwaitTask
                    notOpened <- payResult.Result = PayResult.CouldNotFindRoute
                return ()
            }

        member this.OpenChannelAsync(cashCow: RPCClient, from: ILightningClient, dest: ILightningClient, amount: Money) =
            (this.asyncOpenChannel(cashCow, from, dest, amount) |> Async.StartAsTask).ConfigureAwait(false)

        member this.OpenChannel(cashCow: RPCClient, from: ILightningClient, dest: ILightningClient, amount: Money) =
            this.OpenChannelAsync(cashCow, from, dest, amount).GetAwaiter().GetResult()

        member this.PrepareBTCFundsAsync() =
            let clients = this.GetClients()
            async {
                let! count = clients.Bitcoin.GetBlockCountAsync() |> Async.AwaitTask
                let mat = clients.Bitcoin.Network.Consensus.CoinbaseMaturity
                let notReady = count <= mat
                if notReady then
                    let! _ = clients.Bitcoin.GenerateAsync(mat + 1) |> Async.AwaitTask
                    return ()
                return ()
            }

        member private this.asyncPrepareLNFundsPrivate(amount: Money, confirmation: int option, onlyThisClient: ILightningClient option) =
            let clients = this.GetClients()
            let conf = defaultArg confirmation 3
            async {
                do! this.PrepareBTCFundsAsync()
                match onlyThisClient with
                | Some c ->
                    let! addr = c.GetDepositAddress()|> Async.AwaitTask
                    let! _ = clients.Bitcoin.SendToAddressAsync(addr, amount)|> Async.AwaitTask
                    return! clients.Bitcoin.GenerateAsync(conf)|> Async.AwaitTask
                | None ->
                    let! addr1 = clients.Custody.GetDepositAddress()|> Async.AwaitTask
                    let! tmp = clients.Rebalancer.SwaggerClient.NewWitnessAddressAsync()|> Async.AwaitTask
                    let addr2 = NBitcoin.BitcoinAddress.Create(tmp.Address, network)
                    let! addr3 = clients.ThirdParty.GetDepositAddress()|> Async.AwaitTask
                    let! _ = [addr1; addr2; addr3] |> List.map(fun a -> clients.Bitcoin.SendToAddressAsync(a, amount)) |> Task.WhenAll |> Async.AwaitTask
                    return! clients.Bitcoin.GenerateAsync(conf)|> Async.AwaitTask
            }

        member this.asyncPrepareFunds(amount: Money, ?confirmation: int, ?onlyThisClient: ILightningClient) =
            this.asyncPrepareLNFundsPrivate(amount, confirmation, onlyThisClient)

        member this.PrepareLNFundsAsync(amount: Money, [<Optional>] ?confirmation: int, [<Optional>] ?onlyThisClient: ILightningClient) =
            (this.asyncPrepareLNFundsPrivate(amount, confirmation, onlyThisClient) |> Async.StartAsTask).ConfigureAwait

        member this.PrepareLNFunds(amount: Money, [<Optional>] ?confirmation: int, [<Optional>] ?onlyThisClient: ILightningClient) =
            this.asyncPrepareLNFundsPrivate(amount, confirmation, onlyThisClient) |> Async.RunSynchronously

         member this.Pay(from: ILightningClient, dest: ILightningClient, amountMilliSatoshi: LightMoney) =
            async {
                let! invoice = dest.CreateInvoice(amountMilliSatoshi, "RouteCheckTest", TimeSpan.FromMinutes(5.0), new CancellationToken()) |> Async.AwaitTask
                use! listener = dest.Listen() |> Async.AwaitTask
                let waitTask = listener.WaitInvoice(new CancellationToken())
                let! _ = from.Pay(invoice.BOLT11) |> Async.AwaitTask
                let! paidInvoice = waitTask |> Async.AwaitTask
                return ()
            }


    type LightningNodeLauncher() =

        let getComposeFilePath() =
            let path1 = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../../../../LNTestFramework/docker-compose.yml")) // for testing
            let path2 = Path.GetFullPath(Path.Combine(getAssemblyDirectory(), "../../contentFiles/any/netstandard2.0/docker-compose.yml")) // for production
            if File.Exists(path1) then path1
            else if File.Exists(path2) then path2
            else failwithf "path not found in %s" path2

        member this.createBuilder ([<CallerMemberName>] [<Optional>] ?caller: string, [<Optional>] ?network: Network) =
             let composeFilePath = getComposeFilePath()
             printfn "using compose file %s" composeFilePath
             let name = match caller with
                        | None -> failwith "caller member name not spplyed!"
                        | Some i -> Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), i))
             let net = match network with
                       | Some n -> n
                       | None -> Network.RegTest
             if not (Directory.Exists(name)) then
                Directory.CreateDirectory(name) |> ignore
             else 
                Directory.Delete(name, true)
                Directory.CreateDirectory(name) |> ignore
             new LightningNodeBuilder(name, net, composeFilePath)

    let lnLauncher = new LightningNodeLauncher()