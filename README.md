# Babel Plugins
A collection of plugins for Babel Obfuscator.
Babel Obfuscator is an obuscator for .NET Framework and Mono developed by babelfor.NET

[Babel Obfuscator](http://www.babelfor.net)

Projects and source code are available at GitHub

[Babel Plugins](https://github.com/babelfornet/BabelPlugins)

### DesEncrypt Plugin
The DesEncrypt plugin use .NET Framework Triple DES encryption algorithm to encrypt strings and values. 
The decryption code injected is inside the Code folder and is retrived from managed resources to be
compiled and merged into the target assembly before the obfuscation starts.

**Usage:**
```
babel.exe app.exe --plugin DesEncrypt.dll --string custom --values
```

### ResPacker Plugin
The ResPacker plugin can be used to compress resource streams that are retrieved calling 
the *GetManifestResourceStream* method. The compressed resouce is decompressed at runtime
by replacing the call to GetManifestResourceStream with a call to decompression code.

**Usage:**
```
babel.exe app.exe --plugin ResPacker.dll 
```

### UnreadableNames Plugin
The UnreadableNames plugin show how to implement a simple renaming service.
The service generates unique names varing randomly the characted case of a fixed-lenght name.

**Usage:**
```
babel.exe app.exe --plugin UnreadableNames.dll [--argument namelength=value] [--argument prefixlength=value] [--argument alphabet=string]
```

The resulting names, are quite difficult to interpret because they are all similar to each other.
Example:

```
EwScreykfgcxxtQaMd
EwScreykfgcxxTQaMd
EwScreykfgCxxtqaMd
EwScreykFgCXxtQaMd
```

### AntiDebug Plugin
This plugin adds anti-debugging code that preriodically checks if the process is running in a debugger.
In case a debugger is detected, the process is terminated.
