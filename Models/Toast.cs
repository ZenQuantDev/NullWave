using System;

namespace NullWave.Models;

public class Toast
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Message { get; set; }
    public ToastType Type { get; set; }
    public string Scope { get; set; } = "Main";

    public Toast(string message, ToastType type)
    {
        Message = message;
        Type = type;
    }

    public bool IsSuccess => Type == ToastType.Success;
    public bool IsError => Type == ToastType.Error;
    public bool IsWarning => Type == ToastType.Warning;
    public bool IsInfo => Type == ToastType.Info;
}