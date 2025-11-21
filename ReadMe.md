Dołączyłem plik `sql` wyeksportowany z `IBExpert` na podstawie mojej testowej bazy danych.

### Odnośnie implementacji:
- Aktualnie obsługuje tylko metadane w formacie `sql`,
- W metodzie do eksportu skryptów najpierw ekstrahuję metadane bazy do zdefiniowanych w kodzie modeli, a następnie za pomocą wybranego exportera (w przyszłości można dodać obsługę innych formatów) zapisuję je do plików.
- Metoda do tworzenia bazy danych z metadanych najpierw wczytuje wyeksportowny `header.sql` (odpowiednik nagłówka z metadanych z `IBExpert`) i na podstawie danych z pliku tworzy nową pustą bazę danych `database.fdb`. Następnie łączy się z utworzoną bazą i wywołuje na niej resztę skryptów `sql` w określonej kolejności. Na koniec generowany jest raport.
- Jeśli chodzi o funkcję aktualizacji bazy, to na tą chwilę procedury są aktualizowane, a domeny, tabele dodawane jeśli jeszcze nie istnieją. Tu również na końcu generowany jest raport.

Dodatkowo zostało dodane wsparcie dla `constraints`.

### Odnośnie wymagania dodatkowego:  
Przy implementacji w dużej mierze korzystałem z `MSCopilot`.  
Pomagał mi m.in. z ekstrakcją metadanych z bazy, mapowaniem typów kolumn, i w wielu innych przypadkach.

### Przykładowe wywołania:  
```DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"```  
```DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"```  
```DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"```

W projekcie dodałem też do `launchSettings` testowe wywołania dla każdego z przypadków.