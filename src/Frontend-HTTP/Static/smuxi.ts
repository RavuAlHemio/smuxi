module Smuxi {
    export var messageEpoch: number = -1;
    export var nextMessageID: number = -1;
    export var performMessageUpdate: boolean = true;
    var messageUpdateRunning: boolean = false;

    interface MessagesResponse
    {
        epoch: number;
        nextID: number;
        messages: Message[];
    }

    interface Message
    {
        id: number;
        body: string;
    }

    export function initMessageRefresh(): void {
        document.addEventListener("DOMContentLoaded", function () {
            document.getElementById('message-input').focus();
            updateMessages();
            window.setInterval(updateMessages, 5000);
        });
    }

    function updateMessages(): void {
        if (messageUpdateRunning) {
            return;
        }

        messageUpdateRunning = true;

        var location: Location = window.location;
        // http://localhost:1234/12 -> http://localhost:1234/12/messages/epoch/1/since/1337.json
        var endpoint: string =
            `${location.protocol}//${location.host}${location.pathname}/epoch/${messageEpoch}/since/${nextMessageID}/messages.json${location.search}`;

        var xhr = new XMLHttpRequest();
        xhr.open("GET", endpoint, true);
        xhr.addEventListener("load", () => messagesFetched(xhr));
        xhr.addEventListener("loadend", loadingDone);
        xhr.send();
    }

    function loadingDone(): void {
        messageUpdateRunning = false;
    }

    function messagesFetched(xhr: XMLHttpRequest): void {
        var response: MessagesResponse = JSON.parse(xhr.response);
        if (response.epoch === undefined || response.nextID === undefined) {
            return;
        }

        messageEpoch = response.epoch;
        nextMessageID = response.nextID;

        if (response.messages.length == 0) {
            return;
        }

        var prependString: string = "";
        for (var i: number = 0; i < response.messages.length; ++i) {
            prependString = response.messages[i].body + prependString;
        }

        var messagesElement = document.getElementById('messages');
        if (performMessageUpdate) {
            messagesElement.innerHTML = prependString + messagesElement.innerHTML;
        }
    }
}
