# IPK Projekt 1 - DOKUMENTÁCIA

### Autor: Filip Botlo / xbotlo01

## Zhrnutie zadania 

Cieľom projektu bola implementácia klientskej aplikácie, ktorá komunikuje so serverom cez IPK25-CHAT protokol. Aplikácia môže na základe vybraných argumentov využívať transportný protokol TCP aj UDP.

## Ako sa pri riešení projektu využilo LLM
Pre vypracovanie projektu som používal aj Large Language Model - ChatGPT nasledujúcimi spôsobmi:

- vygenerovanie šablóny pre túto dokumentáciu, sformátovanie zdrojov a výstupov z konzoly pri testovaní
- konzultácia pri návrhu tried a možnostiach .NETu pre tento projekt
- pomoc pri reštruktualizácii kódu pre lepšiu čitateľnosť
- tvorba Makefile
- generovanie niektorých častí kódu ako alternatív k mojim riešeniam

Výstupy LLM boli kontrolované a použité ako doplnkový, najmä konzultačný nástroj. 


## Obsah
- [Krátke teoretické pozadie Transportu](#krátke-teoretické-pozadie)
- [Štruktúra aplikácie](#štruktúra-aplikácie)
- [Implementácia aplikácie](#implementácia-aplikácie)
- [Testovanie a overenie funkcionality](#testovanie-a-overenie-funkcionality)
- [Známe obmedzenia a nedostatky](#známe-obmedzenia-a-nedostatky)
- [Zdroje a použité materiály](#zdroje-a-použité-materiály)


## Krátke teoretické pozadie UDP a TCP

#### TCP (Transmission Control Protocol)

TCP je protokol, ktorý pred prenosom dát spraví tzv. 3-way handshake - klient pošle žiadosť o nadviazanie spojenia cez SYN, Server potvrdí prijatie cez ACK na danú správu a klient to potom potvrdí tiež cez ACK, čím sa nadviaže spojenie. Protokol si čísluje segmenty a kontroluje poradie.

TCP klient v projekte používa triedu `Socket` na pripojenie a posiela správy povolených príkazov (napr AUTH), pri ktorých potom čaká na odpovede od servera. Statový automat potom určuje, ktoré príkazy sú kedy povolené.  

#### UDP (User Datagram Protocol)

UDP je protokol, ktorý nenadväzuje spojenie, takže sa správy pošlu rýchlejšie ale správy sa môžu stratiť.

 UDP klient v projekte využíva `UdpClient` a implementuje logiku spolahlivosti:
 - ku správam je pridelené jedinečné MessageId
 - u spŕav sa čaká na Confirm alebo Reply 
 - pri zlyhaní Confirm správi sa správa posiela znovu
 - stavový automat určuje, ktoré príkazy sú kedy povolené


## Štruktúra aplikácie
**UML diagram s architektúrou tried rozdelenou na TCP a UDP vetvu:**

![UDP Client UML](Doc/UML.PNG)


## Implementácia aplikácie

#### Program.cs
Hlavný vstup programu, ktorý spracuje počiatočné argumenty a podľa prepínača -t určí, či sa vytvorí TCP alebo UDP varianta klienta. Potom sa to spustí cez funkciu `Run` a pre ukončenie voa funckiu `DisconnectAsync`.

Argumenty boli načítané do public record `Arguments` s predvolenými hodnotami pre nepovinné parametre. 
Možné parametre sú:

- `-t` typ protokolu (tcp alebo udp)
- `-s` IP adresa alebo hostname servera
- `-p` port
- `-d` timeout pre UDP
- `-r` maximálny počet retry pokusov pri UDP
- `-h` pomocné hlásenie o parametroch

### TCP Varianta

#### Tcp.cs
Trieda `Tcp` inicializuje Tcp spojenie so serverom cez `Stream Socket` a vytvára pomocné triedy `TcpCommandHandler` a `TcpReciever`.

#### TcpCommandHandler.cs
Trieda `TcpCommandHandler` využíva funckiu `HandleUserInput`, v ktorej číta vstup užívateľa v konzole a potom na to reaguje v závislosti od spŕavnosti príkazu aj aktuálneho stavu. Následne vytvorí `TcpMessage` poďla zadaného príkazu alebo textu (MSG) a potom to pošle cez NetworkStream.

#### TcpReceiver.cs
Trieda prijíma správy zo servera a vypisuje informácie z nich do konzoly pre užívateľa. Rozpoznáva ich pomocou funkcie `ParseTcp()` z treidy `TcpMessage`.

#### TcpMessage.cs
Táto trieda slúži na reprezenaciu všetkých možných správ a na základe typu správy ju potom vo funkcii `ParseTcp()` parsuje alebo vo funkcii `ToTcpString()` konštruuje.

#### TcpStateManager.cs

V tejto triede je udržovaný, nastavovaný a čítaný stav klienta.

### UDP Varianta

#### Udp.cs

Trieda `Udp` Inicializuje triedu `UdpClient` a vytvára pomocnú triedu `UdpReciever`. Číta tu vstup užívateľa v konzole a potom na to reaguje v závislosti od spŕavnosti príkazu aj aktuálneho stavu. Na spolahlivé doručovania správ používa funkciu `SendWithConfirm()` z triedy `UdpConfirmHelper`. 

#### UdpConfirmHelper.cs

Sleduje, či bola konkrétna správa potvrdená podľa jej MessageId a má funkciu `SendConfirmIfNeeded()`,  vďaka ktorej sú správy zo servera automaticky potvrdované.

#### UdpReceiver.cs

Trieda prijíma správy zo servera a vypisuje informácie z nich do konzoly pre užívateľa. Rozpoznáva ich pomocou funkcie `FromBytes()` z tried pre UDP správy.

#### UdpStateManager.cs

V tejto triede je udržovaný, nastavovaný a čítaný stav klienta.

#### UdpMessage

Priečinok UdpMessage obsahuje triedy pre každú UDP spŕavu zvlášť
- `Auth`  
- `Join`  
- `Msg`  
- `Reply`  
- `Err`  
- `Confirm`  
- `Bye`  
- `Ping`  

Triedy majú metódy:
- `ToBytes` pre prevod na bytové pole pri konštrukcii správy
- `FromBytes` pre parsovanie správy zo serveru.
- `Auth` validuje vstupné reťazce cez regex

Bližšie informácie o implementácii možno pozrieť priamo v spomínaných súboroch aj s komentármi.

## Testovanie a overenie funkcionality

### Čo sa testovalo

- Správna inicializácia klienta cez CLI
- Odozva na príkazy užívateľa
- Správy na výstupe podľa špecifikácie
- Odchyt chýb a výpis 

### Prečo sa testovalo

- Správne fungovanie príkazov 
- Zaručenie spoľahlivého prenosu v UDP variante
- Dodržanie postupu komunikácie

### Ako sa testovalo

- Referenčný server `anton5.fit.vutbr.cz`
- Testovanie rôznych príkazov a výstupov na CLI
- Sledovanie komunikácie v aplikácii Wireshark


## Známe obmedzenia a nedostatky


---

## Zdroje a použité materiály
[1] GeeksforGeeks. *Differences between TCP and UDP* [online]. Available at:  
https://www.geeksforgeeks.org/differences-between-tcp-and-udp/ [cited 2025-04-19].

[2] NES@FIT VUT. *IPK Project 2: Client for a chat server using the IPK25-CHAT protocol* [online]. Available at:  
https://git.fit.vutbr.cz/NESFIT/IPK-Projects/src/branch/master/Project_2#udp-transport-summarised [cited 2025-04-19].

[3] Microsoft. *Designing and viewing classes and types (Class Designer)* [online]. Available at:  
https://learn.microsoft.com/en-us/visualstudio/ide/class-designer/designing-and-viewing-classes-and-types?view=vs-2022 (Použité na vytvorenie UML diagramu)














