dotnet new install Microsoft.Azure.Functions.Worker.ProjectTemplates     
dotnet new func -n AzureFunction -F net9.0      

dotnet new install Microsoft.Azure.Functions.Worker.ItemTemplates       
dotnet new http -n Hello -A Anonymous -p:n AzureFunction                

dotnet new aspire-apphost -n AppHost
dotnet new aspire-servicedefaults -n ServiceDefaults

brew tap azure/functions
brew install azure-functions-core-tools@4

echo 'export PATH="/opt/homebrew/opt/azure-functions-core-tools@4/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc

func --version

http://localhost:7284/api/Hello