using System.Text;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Was an der Kopplung stimmen muss, bevor irgendetwas ueber ein fremdes Netz geht.
///
/// Die Zusicherungen hier sind nicht dekorativ. Ein Fehler in dieser Schicht faellt
/// im Betrieb nicht auf - es laeuft weiter, nur ohne den Schutz, den es verspricht.
/// Deshalb wird jede Eigenschaft einzeln geprueft, auch die, die sich von selbst zu
/// verstehen scheint.
/// </summary>
public static class RemoteInvariants
{
    private const string Relay = "relay.steggi-matrix.work";

    public static void Run()
    {
        Check.Group("Kopplung - Schluessel und Raum");

        var key = PairingKey.Create();

        Check.That(key.Text.Length == 43, "Textform ist 43 Zeichen", key.Text.Length.ToString());
        Check.That(key.Text.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'),
                   "Textform ist URL-tauglich", key.Text);

        Check.That(PairingKey.TryParse(key.Text, out PairingKey? again) && again!.RoomId == key.RoomId,
                   "Schluessel ueberlebt den Weg durch den Text");

        Check.That(!PairingKey.TryParse("zu kurz", out _), "Unsinn wird abgelehnt");
        Check.That(!PairingKey.TryParse(null, out _), "nichts wird abgelehnt");
        Check.That(!PairingKey.TryParse(key.Text.Substring(0, 20), out _), "Bruchstueck wird abgelehnt");

        string room = key.RoomId;

        Check.That(room.Length == 32, "Raumkennung ist 32 Zeichen", room.Length.ToString());
        Check.That(room.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f')),
                   "Raumkennung ist Kleinbuchstaben-Hex", room);
        Check.That(key.RoomId == room, "Raumkennung ist bei gleichem Schluessel dieselbe");
        Check.That(PairingKey.Create().RoomId != room, "anderer Schluessel, anderer Raum");
        Check.That(!key.Text.Contains(room, StringComparison.OrdinalIgnoreCase),
                   "die Raumkennung steht nicht im Schluesseltext");

        Check.Group("Kopplung - Sitzungsschluessel");

        byte[] hostSalt = Salt();
        byte[] clientSalt = Salt();

        byte[] toPhone = key.SessionKey(RelayRole.Host, hostSalt, clientSalt);
        byte[] toPc = key.SessionKey(RelayRole.Client, hostSalt, clientSalt);

        Check.That(toPhone.Length == 32, "Sitzungsschluessel ist 256 Bit");
        Check.That(!toPhone.SequenceEqual(toPc), "die Richtungen haben verschiedene Schluessel");

        Check.That(!key.SessionKey(RelayRole.Host, Salt(), clientSalt).SequenceEqual(toPhone),
                   "anderes Salz, anderer Schluessel");

        byte[] nudged = (byte[])clientSalt.Clone();
        nudged[^1] ^= 0x01;

        Check.That(!key.SessionKey(RelayRole.Host, hostSalt, nudged).SequenceEqual(toPhone),
                   "ein gekipptes Bit im Salz reicht");
        Check.That(!PairingKey.Create().SessionKey(RelayRole.Host, hostSalt, clientSalt).SequenceEqual(toPhone),
                   "anderer Kopplungsschluessel, anderer Sitzungsschluessel");

        byte[] hostProof = key.Confirmation(RelayRole.Host, hostSalt, clientSalt);
        Check.That(key.IsConfirmation(hostProof, RelayRole.Host, hostSalt, clientSalt),
                   "Schluesselnachweis stimmt nur mit Schluessel, Rolle und Salzen");
        Check.That(!key.IsConfirmation(hostProof, RelayRole.Client, hostSalt, clientSalt),
                   "Schluesselnachweis gilt nicht fuer die Gegenrolle");
        Check.That(!key.IsConfirmation(hostProof, RelayRole.Host, hostSalt, nudged),
                   "Schluesselnachweis bindet beide Salze");

        Check.Group("Kopplung - Konformitaetsvektor v2.0");

        // Dieser Satz ist kein Zufallstest. Seine Bytes stehen ebenso in
        // PROTOCOL.md und im Android-Test, damit eine Aenderung an HKDF, HMAC,
        // Zaehlern oder dem AES-GCM-Layout sofort als Protokollbruch auffaellt.
        var vectorKey = PairingKey.FromBytes(Hex("030a11181f262d343b424950575e656c737a81888f969da4abb2b9c0c7ced5dc"));
        byte[] vectorHostSalt = Hex("0102030405060708090a0b0c0d0e0f10");
        byte[] vectorClientSalt = Hex("c8c7c6c5c4c3c2c1c0bfbebdbcbbbab9");
        byte[] vectorHelloHost = Hex("020102030405060708090a0b0c0d0e0f10");
        byte[] vectorHelloClient = Hex("02c8c7c6c5c4c3c2c1c0bfbebdbcbbbab9");

        Check.That(vectorKey.Text == "AwoRGB8mLTQ7QklQV15lbHN6gYiPlp2kq7K5wMfO1dw", "Schluesseltext ist festgelegt");
        Check.That(vectorKey.RoomId == "dc5bf478cd0687cb55a2a0ea208c92a3", "Raumkennung ist festgelegt");
        Check.That(SecureChannel.TryReadHello(vectorHelloHost, out byte[]? readHostSalt) &&
                   readHostSalt!.SequenceEqual(vectorHostSalt), "Host-Hello ist festgelegt");
        Check.That(SecureChannel.TryReadHello(vectorHelloClient, out byte[]? readClientSalt) &&
                   readClientSalt!.SequenceEqual(vectorClientSalt), "Client-Hello ist festgelegt");

        Check.That(Convert.ToHexString(vectorKey.SessionKey(RelayRole.Host, vectorHostSalt, vectorClientSalt)).ToLowerInvariant() ==
                   "2bc9e65f2ec34761e7ad45a07b447082459673031dbaaaeeedfe8b8c5988441a",
                   "Host-Sitzungsschluessel ist festgelegt");
        Check.That(Convert.ToHexString(vectorKey.SessionKey(RelayRole.Client, vectorHostSalt, vectorClientSalt)).ToLowerInvariant() ==
                   "bb294b4c98ec57245a38dfa549653763d23028cdbcc6c416424dbedea782de52",
                   "Client-Sitzungsschluessel ist festgelegt");

        byte[] vectorHostProof = vectorKey.Confirmation(RelayRole.Host, vectorHostSalt, vectorClientSalt);
        byte[] vectorClientProof = vectorKey.Confirmation(RelayRole.Client, vectorHostSalt, vectorClientSalt);

        try
        {
            Check.That(Convert.ToHexString(vectorHostProof).ToLowerInvariant() ==
                       "3a2196944745d99953f86e5546196025e45d0726473a3cf8dd5c641bde87a55c",
                       "Host-Nachweis ist festgelegt");
            Check.That(Convert.ToHexString(vectorClientProof).ToLowerInvariant() ==
                       "e71dfd45149334fc580cbc43b70f8d70831e400715c12dbd78a092f7e1062bf5",
                       "Client-Nachweis ist festgelegt");

            using var vectorHost = SecureChannel.Establish(vectorKey, RelayRole.Host, vectorHostSalt, vectorClientSalt);
            using var vectorClient = SecureChannel.Establish(vectorKey, RelayRole.Client, vectorHostSalt, vectorClientSalt);

            byte[] hostProofFrame = vectorHost.Seal(vectorHostProof);
            Check.That(Convert.ToHexString(hostProofFrame).ToLowerInvariant() ==
                       "000000000000000042afa7ac0010fbc7ed549bb70abe72dff29089132d7f7b3414d428cf77979ee65144da862b5288d8121e42f862feb2e4",
                       "Host-Nachweisrahmen hat Zaehler null");
            Check.That(vectorClient.TryOpen(hostProofFrame, out byte[]? openedHostProof) &&
                       vectorKey.IsConfirmation(openedHostProof!, RelayRole.Host, vectorHostSalt, vectorClientSalt),
                       "Client nimmt den Host-Nachweis an");
            Check.That(!vectorClient.TryOpen(hostProofFrame, out _), "Nachweisrahmen ist nicht wiederholbar");

            byte[] clientProofFrame = vectorClient.Seal(vectorClientProof);
            Check.That(Convert.ToHexString(clientProofFrame).ToLowerInvariant() ==
                       "0000000000000000ef721540948959a82f51bdc57f48a793b352c89fcd2c94d61a34fa98bae8792815f64e205736145d53d736d233a3c92f",
                       "Client-Nachweisrahmen hat Zaehler null");
            Check.That(vectorHost.TryOpen(clientProofFrame, out byte[]? openedClientProof) &&
                       vectorKey.IsConfirmation(openedClientProof!, RelayRole.Client, vectorHostSalt, vectorClientSalt),
                       "Host nimmt den Client-Nachweis an");

            byte[] hostApplication = Encoding.UTF8.GetBytes("\u0001{\"t\":\"idle\"}");
            byte[] clientApplication = Encoding.UTF8.GetBytes("\u0001{\"c\":\"preview\",\"w\":320}");

            byte[] hostApplicationFrame = vectorHost.Seal(hostApplication);
            Check.That(Convert.ToHexString(hostApplicationFrame).ToLowerInvariant() ==
                       "000000000000000160f51443554daabc863b543103f01b9d0f8a3306a3a2f4ebd6209d15fe",
                       "erste Host-Nutzlast hat Zaehler eins");
            Check.That(vectorClient.TryOpen(hostApplicationFrame, out byte[]? openedHostApplication) &&
                       openedHostApplication!.SequenceEqual(hostApplication), "Client liest die feste Host-Nutzlast");

            byte[] clientApplicationFrame = vectorClient.Seal(clientApplication);
            Check.That(Convert.ToHexString(clientApplicationFrame).ToLowerInvariant() ==
                       "00000000000000016de0475377372c0f9342cfb7eee029ebc35254a5f8df4b67782ddae84184a6bd826fea4c1698a57a",
                       "erste Client-Nutzlast hat Zaehler eins");
            Check.That(vectorHost.TryOpen(clientApplicationFrame, out byte[]? openedClientApplication) &&
                       openedClientApplication!.SequenceEqual(clientApplication), "Host liest die feste Client-Nutzlast");

            byte[] tampered = (byte[])hostApplicationFrame.Clone();
            tampered[SecureChannel.Overhead - 1] ^= 0x01;

            using var untouchedClient = SecureChannel.Establish(vectorKey, RelayRole.Client, vectorHostSalt, vectorClientSalt);
            Check.That(!untouchedClient.TryOpen(tampered, out _), "gekippter Tag wird verworfen");
            Check.That(!vectorKey.IsConfirmation(vectorHostProof, RelayRole.Client, vectorHostSalt, vectorClientSalt),
                       "Nachweis gilt nicht mit vertauschter Rolle");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(vectorHostProof);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(vectorClientProof);
        }

        Check.Group("Kanal - der Normalfall");

        byte[] helloHost = SecureChannel.Hello(out byte[] saltHost);
        byte[] helloClient = SecureChannel.Hello(out byte[] saltClient);

        Check.That(SecureChannel.TryReadHello(helloHost, out byte[]? readBack) && readBack!.SequenceEqual(saltHost),
                   "Begruessung traegt das Salz");
        Check.That(helloHost[0] == SecureChannel.Version && SecureChannel.Version == 2,
                   "Begruessung verlangt die beglaubigte Fassung");
        Check.That(!saltHost.SequenceEqual(saltClient), "beide Seiten wuerfeln verschieden");

        Check.That(!SecureChannel.TryReadHello(helloHost.AsSpan(0, 8), out _), "zu kurze Begruessung wird abgelehnt");
        Check.That(!SecureChannel.TryReadHello(new byte[1 + SecureChannel.SaltBytes], out _),
                   "fremde Fassung wird abgelehnt");
        Check.That(!SecureChannel.TryReadHello(helloClient.Append((byte)0).ToArray(), out _),
                   "zu lange Begruessung wird abgelehnt");

        using var pc = SecureChannel.Establish(key, RelayRole.Host, saltHost, saltClient);
        using var phone = SecureChannel.Establish(key, RelayRole.Client, saltHost, saltClient);

        byte[] message = Encoding.UTF8.GetBytes("{\"frame\":412,\"samples\":128}");
        byte[] first = pc.Seal(message);

        Check.That(first.Length == message.Length + SecureChannel.Overhead,
                   "Verpackung kostet 24 Bytes", (first.Length - message.Length).ToString());
        Check.That(!first.AsSpan(SecureChannel.Overhead).SequenceEqual(message),
                   "die Nutzlast steht nicht im Klartext darin");

        Check.That(phone.TryOpen(first, out byte[]? opened) && opened!.SequenceEqual(message),
                   "das Handy liest, was der PC schickt");
        Check.That(phone.TryOpen(pc.Seal(message), out _), "und die naechste Nachricht auch");

        byte[] back = phone.Seal(Encoding.UTF8.GetBytes("cancel"));

        Check.That(pc.TryOpen(back, out byte[]? reply) && Encoding.UTF8.GetString(reply!) == "cancel",
                   "der Rueckweg steht ebenso");

        // Eine leere Nutzlast ist erlaubt - ein Lebenszeichen ohne Inhalt.
        Check.That(phone.TryOpen(pc.Seal(ReadOnlySpan<byte>.Empty), out byte[]? nothing) && nothing!.Length == 0,
                   "auch eine leere Nachricht geht durch");

        Check.Group("Kanal - was nicht durchkommen darf");

        using var pcAgain = SecureChannel.Establish(key, RelayRole.Host, saltHost, saltClient);
        using var phoneAgain = SecureChannel.Establish(key, RelayRole.Client, saltHost, saltClient);

        byte[] good = pcAgain.Seal(message);

        Check.That(phoneAgain.TryOpen(good, out _), "die echte Nachricht kommt an");
        Check.That(!phoneAgain.TryOpen(good, out _), "dieselbe Nachricht ein zweites Mal nicht");

        // Jedes einzelne Byte muss zaehlen - Zaehler, Pruefsumme und Geheimtext.
        int survived = 0;

        for (int i = 0; i < good.Length; i++)
        {
            using var fresh = SecureChannel.Establish(key, RelayRole.Client, saltHost, saltClient);

            byte[] bent = (byte[])good.Clone();
            bent[i] ^= 0x01;

            if (fresh.TryOpen(bent, out _)) survived++;
        }

        Check.That(survived == 0, "kein gekipptes Bit kommt durch", survived + " von " + good.Length);

        using var stranger = SecureChannel.Establish(PairingKey.Create(), RelayRole.Client, saltHost, saltClient);
        Check.That(!stranger.TryOpen(good, out _), "mit fremdem Schluessel geht nichts auf");

        using var otherSession = SecureChannel.Establish(key, RelayRole.Client, Salt(), saltClient);
        Check.That(!otherSession.TryOpen(good, out _), "eine Aufnahme taugt in der naechsten Sitzung nicht");

        // Die Gegenrichtung benutzt einen anderen Schluessel. Was der PC sendet, darf
        // er sich nicht selbst wieder aufmachen koennen - sonst waere ein Echo durch
        // den Relay eine gueltige Nachricht.
        using var echo = SecureChannel.Establish(key, RelayRole.Host, saltHost, saltClient);
        Check.That(!echo.TryOpen(good, out _), "ein Echo der eigenen Nachricht wird verworfen");

        using var shortFrames = SecureChannel.Establish(key, RelayRole.Client, saltHost, saltClient);
        Check.That(!shortFrames.TryOpen(new byte[SecureChannel.Overhead - 1], out _),
                   "zu kurzer Rahmen wird abgelehnt");
        Check.That(!shortFrames.TryOpen(ReadOnlySpan<byte>.Empty, out _), "leerer Rahmen wird abgelehnt");

        // Ein Fremder mit erfundenem hohem Zaehler darf die echte Seite nicht aussperren.
        byte[] forged = (byte[])good.Clone();
        forged[7] = 0xFF;

        using var target = SecureChannel.Establish(key, RelayRole.Client, saltHost, saltClient);
        Check.That(!target.TryOpen(forged, out _), "die Faelschung wird verworfen");
        Check.That(target.TryOpen(good, out _), "und sperrt die echte Nachricht nicht aus");

        Check.Group("Einladung");

        var invite = new PairingInvite(key, Relay);

        Check.That(invite.Text.StartsWith("frameflip://pair?", StringComparison.Ordinal),
                   "Einladung traegt das eigene Schema", invite.Text);
        Check.That(PairingInvite.TryParse(invite.Text, out PairingInvite? parsed) &&
                   parsed!.Key.RoomId == key.RoomId && parsed.Relay == Relay,
                   "Einladung ueberlebt den QR-Code");

        Check.That(invite.SocketUrl(RelayRole.Host) == "wss://" + Relay + "/r/" + key.RoomId + "?role=host",
                   "Hostadresse stimmt", invite.SocketUrl(RelayRole.Host));
        Check.That(invite.SocketUrl(RelayRole.Client).EndsWith("?role=client", StringComparison.Ordinal),
                   "Clientadresse stimmt");
        Check.That(invite.SocketUrl(RelayRole.Client).StartsWith("wss://", StringComparison.Ordinal),
                   "immer wss, nie ws");

        Check.That(!PairingInvite.TryParse("frameflip://pair?k=" + key.Text, out _),
                   "ohne Relay keine Einladung");
        Check.That(!PairingInvite.TryParse("frameflip://pair?k=xxx&r=" + Relay, out _),
                   "ohne brauchbaren Schluessel keine Einladung");
        Check.That(!PairingInvite.TryParse("https://pair?k=" + key.Text + "&r=" + Relay, out _),
                   "fremdes Schema wird abgelehnt");
        Check.That(!PairingInvite.TryParse(null, out _), "nichts wird abgelehnt");

        string[] bad = { "../evil", "relay/../x", "host name", "relay:0", "", "-relay.de", "relay..de" };

        foreach (string host in bad)
        {
            Check.That(!PairingInvite.TryParse("frameflip://pair?k=" + key.Text + "&r=" + host, out _),
                       "Relay-Adresse [" + host + "] wird abgelehnt");
        }

        Check.Throws<ArgumentException>(() => new PairingInvite(key, "kein wirt"),
                                        "unbrauchbare Adresse wird nicht angenommen");

        Check.Group("Steuermeldungen des Relays");

        Check.That(RelayControl.Parse("{\"t\":\"waiting\"}", out _) == RelayMessage.Waiting, "waiting");
        Check.That(RelayControl.Parse("{\"t\":\"peer\",\"up\":true}", out _) == RelayMessage.PeerUp, "peer up");
        Check.That(RelayControl.Parse("{\"t\":\"peer\",\"up\":false}", out _) == RelayMessage.PeerDown, "peer down");

        Check.That(RelayControl.Parse("{\"t\":\"error\",\"why\":\"role already taken\"}", out string? why)
                   == RelayMessage.Error && why == "role already taken",
                   "error mit Begruendung", why);

        // Was sich nicht lesen laesst, wird verworfen - nicht geworfen. Eine spaetere
        // Fassung des Relays darf Meldungen hinzufuegen, ohne diese Seite zu brechen.
        string[] junk =
        {
            "", "   ", "kein json", "[]", "\"text\"", "42", "null",
            "{}", "{\"t\":42}", "{\"t\":\"peer\"}", "{\"t\":\"peer\",\"up\":\"ja\"}",
            "{\"t\":\"etwas-neues\"}", "{\"t\":\"waiting\"", "{\"t\":\"error\"}"
        };

        int surprises = 0;

        foreach (string text in junk)
        {
            var parsed2 = RelayControl.Parse(text, out _);

            // "error" ohne Grund ist immer noch ein Fehler - der Rest ist unbekannt.
            var expected = text == "{\"t\":\"error\"}" ? RelayMessage.Error : RelayMessage.Unknown;

            if (parsed2 != expected) surprises++;
        }

        Check.That(surprises == 0, "Unlesbares wird verworfen, nicht geworfen", surprises.ToString());
        Check.That(RelayControl.Parse(null, out _) == RelayMessage.Unknown, "nichts wird verworfen");
    }

    private static byte[] Salt()
    {
        byte[] salt = new byte[SecureChannel.SaltBytes];
        Random.Shared.NextBytes(salt);
        return salt;
    }

    private static byte[] Hex(string text) => Convert.FromHexString(text);
}
