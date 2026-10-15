# Play Console: closed testing checklist

## 1. Unity project (done)

- Package name `rootlevel.it.outofthebarn`, product name "Out of the Barn", company "RootLevel.It"
- Version 0.1.0, version code 1. **Raise the version code for every upload** (Player Settings › Other Settings › Version code).
- IL2CPP, ARM64 only, minimum API 26 (Android 8.0), target API 36
- Portrait only, release (non-development) build, output = Android App Bundle (.aab)
- Launcher icon: adaptive (meadow background + cow foreground) from `Assets/Icons/`

## 2. Signing (you)

1. Unity › Project Settings › Player › Android › Publishing Settings › Keystore Manager: create (or pick) your keystore and an upload key alias.
2. Keep the keystore file and both passwords somewhere safe and **outside the repository**. Unity does not save the passwords; you enter them again after reopening the project.
3. Build: File › Build Profiles › Android: untick **Export Project** (it is on in this project; with it on Unity writes a Gradle project folder instead of a bundle), then Build. The result is a single `<name>.aab` file, about 39 MB (an unsigned test build on 2026-10-15 succeeded with 0 errors).
4. In Play Console, keep **Play App Signing** on (the default): Google holds the app signing key and your keystore is only the upload key, so a lost upload key can be reset.

## 3. Create the app

All apps › Create app: name "Out of the Barn", default language, **Game**, **Free**, accept the declarations.

## 4. Store listing (Grow › Store presence › Main store listing)

Copy texts from `listing.md`, upload `icon-512.png`, `feature-graphic-1024x500.png` and the six phone screenshots. Category: Puzzle. Add your contact email.

## 5. App content (Policy › App content): suggested answers

| Section | Answer |
|---|---|
| Privacy policy | URL where `privacy-policy.md` is published (it must be a public web page) |
| Ads | No, the app does not contain ads |
| App access | All functionality is available without special access |
| Content rating | IARC questionnaire, category Game: no violence, fear, sexuality, language, drugs, gambling, user interaction or data sharing; expected result PEGI 3 / Everyone |
| Target audience | Choose your age groups. Picking any group under 13 brings the Families policy and an extra review; the game qualifies (no ads, no data), but it adds steps. 13+ is the quickest route for a test. |
| Data safety | "Does your app collect or share any of the required user data types?" No. The form then skips the per-data questions. No account creation. |
| Government app / financial / health | No |

## 6. Closed testing track

1. Test and release › Testing › Closed testing › Create track (or use "Alpha").
2. Testers: add an email list or a Google Group.
3. Create release › upload the `.aab` › release name "0.1.0 (1)" › release notes › Review › Start rollout.
4. Share the opt-in link with testers; they join, then install from Play.

**New personal developer accounts** (created after Nov 13, 2023) need a closed test with **at least 12 testers opted in for 14 days in a row** before they can apply for production access. The Console's dashboard shows the current numbers for your account.
