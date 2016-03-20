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