"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/solutionStatusHub").build();
// https://github.com/aspnet/AspNetCore.Docs/blob/master/aspnetcore/signalr/groups/sample/wwwroot/js/chat.js
//Disable send button until connection is established
//document.getElementById("sendButton").disabled = true;

connection.on("newSolutionStatus", function (message) {
    var msg = message.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    var encodedMsg = "Received message:" + msg;
    var li = document.createElement("li");
    li.textContent = encodedMsg;
    document.getElementById("messagesList").appendChild(li);
});

document.getElementById("join-group").addEventListener("click", async (event) => {
    var groupName = document.getElementById("group-name").value;
    try {
        await connection.invoke("AddToGroup", groupName);
    }
    catch (e) {
        console.error(e.toString());
    }
    event.preventDefault();
});
document.getElementById("leave-group").addEventListener("click", async (event) => {
    var groupName = document.getElementById("group-name").value;
    try {
        await connection.invoke("RemoveFromGroup", groupName);
    }
    catch (e) {
        console.error(e.toString());
    }
    event.preventDefault();
});

(async () => {
    try {
        await connection.start();
    }
    catch (e) {
        console.error(e.toString());
    }
})();