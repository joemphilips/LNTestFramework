namespace LNTestFramework

open System
open System.IO
open System.Reflection
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open NBitcoin
open BTCPayServer.Lightning

module Utils =
    open SwaggerProvider

    let getAssemblyDirectory () =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) |> Path.GetFullPath

    let [<Literal>] schema = "https://github.com/lightningnetwork/lnd/raw/master/lnrpc/rpc.swagger.json"
    type LNDSwaggerProvider = SwaggerProvider<schema>

    type LNDSwaggerProvider with
        member this.AsILightningClient =
            {
                new ILightningClient with
                member __.GetInvoice (invoice: string, token: CancellationToken) = 
                    task {
                        let invoice = new LNDSwaggerProvider.lnrpcInvoice()
                        let hoge = this.AddInvoice(invoice)
                        return new LightningInvoice()
                        }
                member __.CreateInvoice(amount: LightMoney, desc: string, expiry: TimeSpan, token) =
                    task {
                        return new LightningInvoice()
                        }
                member __.Listen (token) =
                    task {
                        return { new ILightningInvoiceListener with
                                member ___.Dispose() = ()
                                member ___.WaitInvoice(token) = task { return LightningInvoice() }
                            }
                        }
                member __.GetInfo (token) =
                    task {
                        return new LightningNodeInformation()
                    }
                member __.Pay(bolt11: string, token) =
                    task {
                        return new PayResponse(PayResult.Ok)
                    }
                member __.OpenChannel(request, token) =
                    task {
                        return new OpenChannelResponse(OpenChannelResult.AlreadyExists)
                    }
                member __.GetDepositAddress() =
                    task {
                        let addr = this.NewAddress()
                        let info = this.GetInfo()
                        return BitcoinAddress.Create(addr.Address)
                    }
                 member __.ConnectTo(info: NodeInfo) =
                     task {
                         return ()
                     } :> Task
            } 
