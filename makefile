start-database:
	docker-compose -f docker-compose.yml up postgres admin

update-database:
	dotnet run --project Sandbox.Updater/Sandbox.Updater.csproj
