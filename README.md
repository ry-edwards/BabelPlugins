# Babel Plugins
A collection of plugin projects for Babel Obfuscator.
Babel Obfuscator is an obuscator for .NET Framework and Mono developed by babelfor.NET
[Babel Obfuscator](http://www.babelfor.net)

### DesEncrypt Plugin
The DesEncrypt plugin use .NET Framework Triple DES encryption algorithm to encrypt strings and values. 
The decryption code injected is inside the Code folder and is retrived from managed resources to be
compiled and merged into the target assembly before the obfuscation starts.

**Usage:**
```
babel.exe app.exe --plugin DesEncrypt.dll --string custom --values
```

