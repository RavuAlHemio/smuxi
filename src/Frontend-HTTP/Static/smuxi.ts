interface Window {
    Notification?: NotificationInterface;
}

interface NotificationInterface {
    requestPermission(): PromiseLike<void>;
}

interface NotificationOptions {
    dir?: "auto"|"ltr"|"rtl";
    lang?: string;
    badge?: string;
    body?: string;
    tag?: string;
    icon?: string;
    data?: any;
    vibrate?: number[];
    renotify?: boolean;
    silent?: boolean;
    sound?: string;
    noscreen?: boolean;
    sticky?: boolean;
}

declare class Notification {
    constructor(title: string, options?: NotificationOptions);
}

module Smuxi {
    export var messageEpoch: number = -1;
    export var nextMessageID: number = -1;
    export var performMessageUpdate: boolean = true;
    export var notifyOnHighlight: boolean = false;
    var messageUpdateRunning: boolean = false;
    var firstFetch: boolean = true;
    var chatName: string;
    var updateIntervalHandle: number;

    interface MessagesResponse {
        epoch: number;
        nextID: number;
        messages: Message[];
        highlighted: boolean;
    }

    interface Message {
        id: number;
        body: string;
    }

    export function initMessageRefresh(): void {
        document.addEventListener("DOMContentLoaded", function () {
            document.getElementById('message-input').focus();
            updateMessages();
            updateIntervalHandle = window.setInterval(updateMessages, 5000);
        });
    }

    export function initNotifications(): void {
        document.addEventListener("DOMContentLoaded", function () {
            if (!("Notification" in window)) {
                return;
            }
            chatName = (<HTMLMetaElement>document.head.querySelector("meta[name='smuxi:chat']")).content;
            window.Notification.requestPermission().then(function () {
                notifyOnHighlight = true;
            });
        });
    }

    export function changeInterval(interval: number): void {
        window.clearInterval(updateIntervalHandle);
        updateIntervalHandle = window.setInterval(updateMessages, interval);
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
        firstFetch = false;
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

        if (notifyOnHighlight && response.highlighted && !firstFetch) {
            new Notification(chatName, {body: "You have been highlighted."});
        }
    }
}
