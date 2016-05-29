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

        var endpoint: string = window.location.href + "/messages";

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
