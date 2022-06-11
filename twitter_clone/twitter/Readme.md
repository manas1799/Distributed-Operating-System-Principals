# Project 4 Part II : Twitter Clone

## Group Members

- Ritin Bhardwaj (UFID: 8296-1815)
- Manas Gupta (UFID: 6025-9718)

## Functionality

- This project is the continuation of the project4 part 1.
- Here we have used the Suave package to generate the operations like GET and POST to implement the various functionalities like login a user, tweet etc. which were done in the AKKA.NET framework of F# in project 4 part 1. The data is stored as a JSON file.

## How to Run

- The code executes by first starting the websockets server at the localhost 8080
  using the following command in the directory where the program.fs file exists:Command to run the program:

  `dotnet run`

- Next, the client will send various requests like registering a new user, logging in a registered user, etc.
- Various requests can be of the format:

  - (POST) Requests to add a new user:
    `http://localhost:8080/register`

    ```
    {
     "user":"user1",
     "value":"user1@123"
    }
    {
     "user":"user2",
     "value":"user2@123"
    }

    ```

  - (POST) Login an user:
  - `http://localhost:8080/login`
  - ```
    {
     "user":"user1",
     "value":"user1@123"
    }
    {
     "user":"user2",
     "value":"user2@123"
    }

    ```

  - (POST) Follow a user:
  - `http://localhost:8080/follow`

    ```

    {
        "user":"user2",
        "value":"user1"
    }
    ```

  - (POST) Make a tweet:
  - `http://localhost:8080/tweet`

    ```
    {
        "user":"user1",
        "value": "User 1 tweeted this!"
    }
    ```

  - Make a retweet
  - `http://localhost:8080/retweet`

    ```
    {
        "user":"user2",
        "value":"user1: User 1 tweeted this!"
    }
    ```

  - Search a tweet in the database:
  - `http://localhost:8080/search/parameter`

    ```
        parameter : #hashtagName, @user_ID
    ```
