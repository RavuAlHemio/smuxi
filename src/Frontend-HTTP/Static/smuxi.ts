module Smuxi {
    export var performMessageUpdate: boolean = true;
    var messageUpdateRunning: boolean = false;

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
        // http://localhost:1234/12 -> http://localhost:1234/12/messages
        var endpoint: string = `${location.protocol}//${location.host}${location.pathname}/messages${location.search}`;

        var xhr = new XMLHttpRequest();
        xhr.open("GET", endpoint, true);
        xhr.addEventListener("loadend", () => messagesFetched(xhr));
        xhr.send();
    }

    function messagesFetched(xhr: XMLHttpRequest): void {
        messageUpdateRunning = false;

        var messagesElement = document.getElementById('messages');
        if (performMessageUpdate) {
            messagesElement.innerHTML = xhr.response;
        }
    }
}
