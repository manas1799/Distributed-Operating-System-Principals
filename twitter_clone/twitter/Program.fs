open Akka.FSharp
open FSharp.Json
open Akka.Actor
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Newtonsoft.Json
open Newtonsoft.Json.Serialization

let system = ActorSystem.Create("System")


let mutable hash_map = Map.empty
let mutable mentions_map = Map.empty
let mutable registeredUsers_map = Map.empty
let mutable followers_map = Map.empty




type Response = {
  userID: string
  message: string
  service: string
  code: string
}

type Request = {
  userID: string
  value: string
}


type Showfeed = 
  | RegisterNewUser of (string)
  | Subscribers of (string*string)
  | AddOnlineUsers of (string*WebSocket)
  | RemoveOnlineUsers of (string)
  | UpdateFeeds of (string*string*string)

type TweetMessages = 
  | InitTweet of (IActorRef)
  | AddRegisteredUser of (string)
  | TweetRequest of (string*string*string)

type RestResource<'a> = {
    EntryType : Request -> string
}

let client = MailboxProcessor<string*WebSocket>.Start(fun inbox ->
  let rec messageLoop() = async {
    let! msg,webSkt = inbox.Receive()
    let byteRes =
      msg
      |> System.Text.Encoding.ASCII.GetBytes
      |> ByteSegment
    let! _ = webSkt.send Text byteRes true
    return! messageLoop()
  }
  messageLoop()
)

let Supervisor (mailbox:Actor<_>) = 
  let mutable followers = Map.empty
  let mutable OnlineUsers = Map.empty
  let mutable feedtable = Map.empty
  let rec loop () = actor {
      let! message = mailbox.Receive() 
      match message with
      //register new user
      | RegisterNewUser(userId) ->
        followers <- Map.add userId Set.empty followers
        feedtable <- Map.add userId List.empty feedtable
    
      //Add followers
      | Subscribers(userId, followID) ->

        if followers.ContainsKey followID then
          let mutable followSet = Set.empty
          followSet <- followers.[followID]
          followSet <- Set.add userId followSet
          followers <- Map.remove followID followers 
          followers <- Map.add followID followSet followers
          let mutable jsonData: Response = {userID = followID; service= "Follow"; code = "OK"; message = sprintf "User %s started following you!" userId}
          let mutable jsonString = Json.serialize jsonData
          client.Post (jsonString,OnlineUsers.[followID])

      //update user feeds
      | UpdateFeeds(userId,tweetMsg,sertype) ->

        if followers.ContainsKey userId then
          let mutable stype = ""

          if sertype = "Tweet" then
            stype <- sprintf "%s tweeted:" userId

          else 
            stype <- sprintf "%s re-tweeted:" userId

          for i in followers.[userId] do 
            if followers.ContainsKey i then
              if OnlineUsers.ContainsKey i then
                let twt = sprintf "%s^%s" stype tweetMsg
                let jsonData: Response = {userID = i; service=sertype; code="OK"; message = twt}
                let jsonString = Json.serialize jsonData
                client.Post (jsonString,OnlineUsers.[i])

              let mutable listy = []

              if feedtable.ContainsKey i then
                  listy <- feedtable.[i]

              listy  <- (sprintf "%s^%s" stype tweetMsg) :: listy
              feedtable <- Map.remove i feedtable
              feedtable <- Map.add i listy feedtable

      //add online users to map
      | AddOnlineUsers(userId,userWebSkt) ->
        if OnlineUsers.ContainsKey userId then  
          OnlineUsers <- Map.remove userId OnlineUsers
        
        OnlineUsers <- Map.add userId userWebSkt OnlineUsers 
        let mutable publishFeed = ""
        let mutable sertype = ""
        
        if feedtable.ContainsKey userId then
          let mutable feedsTop = ""
          let mutable fSize = 10
          let feedList:List<string> = feedtable.[userId]
          
          if feedList.Length = 0 then
            sertype <- "Follow"
            publishFeed <- sprintf "No feeds yet!!"
          
          else
            
            if feedList.Length < 10 then
                fSize <- feedList.Length
            
            for i in [0..(fSize-1)] do
              feedsTop <- "-" + feedtable.[userId].[i] + feedsTop

            publishFeed <- feedsTop
            sertype <- "LiveFeed"

          let jsonData: Response = {userID = userId; message = publishFeed; code = "OK"; service=sertype}
          let jsonString = Json.serialize jsonData
          client.Post (jsonString,userWebSkt) 

       //remove online users from map
      | RemoveOnlineUsers(userId) ->

        if OnlineUsers.ContainsKey userId then  
          OnlineUsers <- Map.remove userId OnlineUsers

      

      return! loop()
  }
  loop()

//supervisor actor
let supervisor = spawn system (sprintf "Supervisor") Supervisor

let liveFeed (webSocket : WebSocket) (context: HttpContext) =
  let rec loop() =
    let mutable presentUser = ""
    socket { 
      let! msg = webSocket.read()
      match msg with

      | (Text, data, true) ->
        let reqMsg = UTF8.toString data
        let parsed = Json.deserialize<Request> reqMsg
        presentUser <- parsed.userID
        supervisor <! AddOnlineUsers(parsed.userID, webSocket)
        return! loop()

      | (Close, _, _) ->
        printfn "Closed WEBSOCKET"
        supervisor <! RemoveOnlineUsers(presentUser)
        let emptyResponse = [||] |> ByteSegment
        do! webSocket.send Close emptyResponse true

      | _ -> return! loop()
    }
  loop()

//search for hashtags and mentions
let query (keyValue:string) = 
  let mutable tagsstring = ""
  let mutable mentionsString = ""
  let mutable resp = ""
  let mutable size = 10
  //search for query
  if keyValue.Length > 0 then
    //query contains hashtag
    if keyValue.[0] <> '@' then
      let searchKey = keyValue

      if hash_map.ContainsKey searchKey then
        let mapData:List<string> = hash_map.[searchKey]

        if (mapData.Length < 10) then
            size <- mapData.Length

        for i in [0..(size-1)] do
            tagsstring <- tagsstring + "-" + mapData.[i]

        let rectype: Response = {userID = ""; service="Query"; message = tagsstring; code = "OK"}
        resp <- Json.serialize rectype

      else 
        let rectype: Response = {userID = ""; service="Query"; message = "-No tweets found for the hashtag"; code = "OK"}
        resp <- Json.serialize rectype
      
    //query contains mention
    else
      let searchKey = keyValue.[1..(keyValue.Length-1)]

      if mentions_map.ContainsKey searchKey then
        let mapData:List<string> = mentions_map.[searchKey]

        if (mapData.Length < 10) then
          size <- mapData.Length

        for i in [0..(size-1)] do
          mentionsString <- mentionsString + "-" + mapData.[i]

        let rectype: Response = {userID = ""; service="Query"; message = mentionsString; code = "OK"}
        resp <- Json.serialize rectype

      else 
        let rectype: Response = {userID = ""; service="Query"; message = "-No tweets found for the mentioned user"; code = "OK"}
        resp <- Json.serialize rectype
      
  //query search failed
  else
    let rectype: Response = {userID = ""; service="Query"; message = "Type something to search"; code = "FAIL"}
    resp <- Json.serialize rectype

  resp

//function to tweet
let tweetUser keyValue =
  let mutable resp = ""
  //check if user is registered
  if registeredUsers_map.ContainsKey keyValue.userID then
    let mutable hashTag = ""
    let mutable mentionedUser = ""
    let parsed = keyValue.value.Split ' '

    for parse in parsed do
      if parse.Length > 0 then
        if parse.[0] = '#' then
          hashTag <- parse.[1..(parse.Length-1)]

        else if parse.[0] = '@' then
          mentionedUser <- parse.[1..(parse.Length-1)]

    if mentionedUser <> "" then
      if registeredUsers_map.ContainsKey mentionedUser then
        if not (mentions_map.ContainsKey mentionedUser) then
            mentions_map <- Map.add mentionedUser List.empty mentions_map

        let mutable mList = mentions_map.[mentionedUser]
        mList <- (sprintf "%s tweeted:^%s" keyValue.userID keyValue.value) :: mList
        mentions_map <- Map.remove mentionedUser mentions_map
        mentions_map <- Map.add mentionedUser mList mentions_map
        supervisor <! UpdateFeeds(keyValue.userID,keyValue.value,"Tweet")
        let rectype: Response = {userID = keyValue.userID; service="Tweet"; message = (sprintf "%s tweeted:^%s" keyValue.userID keyValue.value); code = "OK"}
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

      else
        let rectype: Response = {userID = keyValue.userID; service="Tweet"; message = sprintf "Invalid request, mentioned user (%s) is not registered" mentionedUser; code = "FAIL"}
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

    else
      supervisor <! UpdateFeeds(keyValue.userID,keyValue.value,"Tweet")
      let rectype: Response = {userID = keyValue.userID; service="Tweet"; message = (sprintf "%s tweeted:^%s" keyValue.userID keyValue.value); code = "OK"}
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

    if hashTag <> "" then
      if not (hash_map.ContainsKey hashTag) then
        hash_map <- Map.add hashTag List.empty hash_map

      let mutable tList = hash_map.[hashTag]
      tList <- (sprintf "%s tweeted:^%s" keyValue.userID keyValue.value) :: tList
      hash_map <- Map.remove hashTag hash_map
      hash_map <- Map.add hashTag tList hash_map

  else  
    let rectype: Response = {userID = keyValue.userID; service="Tweet"; message = sprintf "Invalid request by user %s, user not registed" keyValue.userID; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  resp

//function to regiser new user
let regNewUser keyValue =
  let mutable resp = ""

  if registeredUsers_map.ContainsKey keyValue.userID then
    let rectype: Response = {userID = keyValue.userID; message = sprintf " %s is registred already" keyValue.userID; service = "Register"; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  else
    registeredUsers_map <- Map.add keyValue.userID keyValue.value registeredUsers_map
    followers_map <- Map.add keyValue.userID Set.empty followers_map
    supervisor <! RegisterNewUser(keyValue.userID)
    let rectype: Response = {userID = keyValue.userID; message = sprintf "%s registred successfully" keyValue.userID; service = "Register"; code = "OK"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  resp

//login user
let loginUser keyValue =
  let mutable resp = ""

  if registeredUsers_map.ContainsKey keyValue.userID then
    if registeredUsers_map.[keyValue.userID] = keyValue.value then
      let rectype: Response = {userID = keyValue.userID; message = sprintf "%s logged in successfully" keyValue.userID; service = "Login"; code = "OK"}
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

    else 
      let rectype: Response = {userID = keyValue.userID; message = "Invalid userID / Password"; service = "Login"; code = "FAIL"}
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  else
    let rectype: Response = {userID = keyValue.userID; message = "Invalid userID / Password"; service = "Login"; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  resp

//Follow User
let followUser keyValue =
  let mutable resp = ""

  if keyValue.value <> keyValue.userID then
    //check if user is registered
    if followers_map.ContainsKey keyValue.value then
      if not (followers_map.[keyValue.value].Contains keyValue.userID) then
        let mutable tempset = followers_map.[keyValue.value]
        tempset <- Set.add keyValue.userID tempset
        followers_map <- Map.remove keyValue.value followers_map
        followers_map <- Map.add keyValue.value tempset followers_map
        supervisor <! Subscribers(keyValue.userID,keyValue.value) 
        let rectype: Response = {userID = keyValue.userID; service="Follow"; message = sprintf "You started following %s!" keyValue.value; code = "OK"}
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

      else 
        let rectype: Response = {userID = keyValue.userID; service="Follow"; message = sprintf "You already follows %s!" keyValue.value; code = "FAIL"}
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString      

    else  
      let rectype: Response = {userID = keyValue.userID; service="Follow"; message = sprintf "No such user (%s)." keyValue.value; code = "FAIL"}
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  else
    let rectype: Response = {userID = keyValue.userID; service="Follow"; message = sprintf "You cannot follow yourself."; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString    

  resp
  

//Retweet User tweet
let retweetUser keyValue =
  let mutable resp = ""

  if registeredUsers_map.ContainsKey keyValue.userID then
    supervisor <! UpdateFeeds(keyValue.userID,keyValue.value,"ReTweet")
    let rectype: Response = {userID = keyValue.userID; service="ReTweet"; message = (sprintf "%s re-tweeted: ^%s" keyValue.userID keyValue.value); code = "OK"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  else  
    let rectype: Response = {userID = keyValue.userID; service="ReTweet"; message = sprintf "Invalid request by %s, User not registered" keyValue.userID; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

  resp


//JSON response serelialze string into JSON object
let respInJson x =     
    let jsonSerializerSettings = JsonSerializerSettings()
    jsonSerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    JsonConvert.SerializeObject(x, jsonSerializerSettings)
    |> OK 
    >=> Writers.setMimeType "application/json; charset=utf-8"

//deserialize JSON object
let fromJson<'a> json =
        JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a    

let getReqResource<'a> (requestInp : HttpRequest) = 
    let getInString (rawForm:byte[]) = System.Text.Encoding.UTF8.GetString(rawForm)
    let requestArr:byte[] = requestInp.rawForm
    requestArr |> getInString |> fromJson<Request>

let entryRequest resourceName resource = 
  let resourcePath = "/" + resourceName

  let entryDone keyValue =
    let userRegResp = resource.EntryType keyValue
    userRegResp

  choose [
    path resourcePath >=> choose [
      POST >=> request (getReqResource >> entryDone >> respInJson)
    ]
  ]

//Entry point for registering new user
let RegisterEntryPoint = entryRequest "register" {
  EntryType = regNewUser
}

//Entry point for login
let LoginEntryPoint = entryRequest "login" {
  EntryType = loginUser
}

//Entry point for tweet
let TweetEntryPoint = entryRequest "tweet" {
  EntryType = tweetUser
}

//Entry point for retweet
let ReTweetEntryPoint = entryRequest "retweet" {
  EntryType = retweetUser
}

//Entry point for following user
let FollowEntryPoint = entryRequest "follow" {
  EntryType = followUser
}

let app = 
  choose [
    path "/livefeed" >=> handShake liveFeed
    RegisterEntryPoint
    LoginEntryPoint
    FollowEntryPoint
    TweetEntryPoint
    ReTweetEntryPoint
    pathScan "/search/%s"
      (fun searchkey ->
        let keyval = (sprintf "%s" searchkey)
        let reply = query keyval
        OK reply) 
    GET >=> choose [path "/" >=> OK "Welcome! Twitter Clone Project4_2"]
  ]

//start web server with configuration app
startWebServer defaultConfig app