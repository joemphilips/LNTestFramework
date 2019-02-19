# Lightning Network Testing Framework

**The status of this package is in pre-alpha. It is extremely buggy and lacks crucial features**

This is a library for launching several Lightning network node instance using `docker-compose` and perform operation on it

## Usage

Look [csharp tests](https://github.com/joemphilips/LNTestFramework/tree/master/LNTestFramework.Tests.CSharp) for example.

F# tests are not ready yet.

## TODO

* add fsharp tests.
* expose every public methods both `Async` and `Task` return type.
* enable to generate new docker-compose for each builder (for testing different topology).
* create new computation expressoion `lntest`
